import assert from 'node:assert';
import { test, before, describe } from 'node:test';
import { ethers } from 'ethers';
import { createEVMRunner } from './vm-runner.js';

describe('SigilToken and MetadataPointer Test Suite (EVM JS)', () => {
  let runner;
  let deployerHex = '0x1000000000000000000000000000000000000001';
  let authorityHex = '0x2000000000000000000000000000000000000002';
  let user1Hex     = '0x3000000000000000000000000000000000000003';
  let user2Hex     = '0x4000000000000000000000000000000000000004';

  let sigilToken;
  let metadataStore1;
  let metadataStore2;

  before(async () => {
    runner = await createEVMRunner();
  });

  test('Deploy SigilMetadataStore and SigilToken', async () => {
    metadataStore1 = await runner.deployContract('SigilMetadataStore.sol', 'SigilMetadataStore', [''], deployerHex);
    assert.ok(metadataStore1.address, 'MetadataStore 1 deployed address should exist');

    const initialSupply = ethers.parseEther('1000000');
    sigilToken = await runner.deployContract(
      'SigilToken.sol',
      'SigilToken',
      ['DBD Sigil Token', 'SIGIL', initialSupply, metadataStore1.address, authorityHex],
      deployerHex
    );

    assert.ok(sigilToken.address, 'SigilToken deployed address should exist');

    assert.strictEqual(await sigilToken.call('name'), 'DBD Sigil Token');
    assert.strictEqual(await sigilToken.call('symbol'), 'SIGIL');
    assert.strictEqual(await sigilToken.call('totalSupply'), initialSupply);
    assert.strictEqual(await sigilToken.call('balanceOf', [deployerHex]), initialSupply);
    assert.strictEqual((await sigilToken.call('metadataPointer')).toLowerCase(), metadataStore1.address.toLowerCase());
    assert.strictEqual((await sigilToken.call('pointerAuthority')).toLowerCase(), authorityHex.toLowerCase());
    assert.strictEqual(await sigilToken.call('isPointerLocked'), false);
  });

  test('Transfer and allowance functionality', async () => {
    const amount = ethers.parseEther('1000');
    await sigilToken.send('transfer', [user1Hex, amount], deployerHex);
    assert.strictEqual(await sigilToken.call('balanceOf', [user1Hex]), amount);

    // Approve user2
    await sigilToken.send('approve', [user2Hex, amount], user1Hex);
    assert.strictEqual(await sigilToken.call('allowance', [user1Hex, user2Hex]), amount);

    // TransferFrom by user2
    await sigilToken.send('transferFrom', [user1Hex, user2Hex, amount], user2Hex);
    assert.strictEqual(await sigilToken.call('balanceOf', [user2Hex]), amount);
    assert.strictEqual(await sigilToken.call('balanceOf', [user1Hex]), 0n);
  });

  test('Resolve Metadata via MetadataPointer', async () => {
    // Configure tokenId 1 in metadataStore1 (Pathway 0: Public, Level 1)
    await metadataStore1.send('setTokenPathwayAndLevel', [1, 0, 1], deployerHex);

    const rawMetadata = await sigilToken.call('tokenURI', [1]);
    const parsed = JSON.parse(rawMetadata);

    assert.strictEqual(parsed.name, 'DBD Sigil Token #1 - Public');
    assert.strictEqual(parsed.symbol, 'SIGIL');
    assert.ok(parsed.image.includes('<svg'), 'Image should contain on-chain SVG');
    assert.ok(parsed.image.includes('Level 1'), 'SVG should contain Level 1 text');

    const attrs = await sigilToken.call('getSigilAttributes', [1]);
    assert.strictEqual(attrs.name, 'DBD Sigil Token #1 - Public');
    assert.strictEqual(attrs.pathway, 0n); // Public
    assert.strictEqual(attrs.level, 1n);
  });

  test('Dynamic SVG rendering across pathways (Public, Members, Keepers)', async () => {
    // Pathway 1: Members (#7992d6)
    const membersSvg = await metadataStore1.call('getSigilSVG', [1, 2]);
    assert.ok(membersSvg.includes('#7992d6'), 'Members sigil should use purple-blue theme');
    assert.ok(membersSvg.includes('Level 2'), 'Should reflect Level 2');

    // Pathway 2: Keepers (#d67979)
    const keepersSvg = await metadataStore1.call('getSigilSVG', [2, 3]);
    assert.ok(keepersSvg.includes('#d67979'), 'Keepers sigil should use red theme');
    assert.ok(keepersSvg.includes('Level 3'), 'Should reflect Level 3');
  });

  test('Update MetadataPointer by pointerAuthority', async () => {
    metadataStore2 = await runner.deployContract(
      'SigilMetadataStore.sol',
      'SigilMetadataStore',
      ['https://api.dbd-sigil.org/metadata'],
      deployerHex
    );

    await sigilToken.send('updateMetadataPointer', [metadataStore2.address], authorityHex);
    assert.strictEqual((await sigilToken.call('metadataPointer')).toLowerCase(), metadataStore2.address.toLowerCase());

    await metadataStore2.send('setTokenPathwayAndLevel', [2, 2, 5], deployerHex); // Keepers, Level 5
    const rawMetadata2 = await sigilToken.call('tokenURI', [2]);
    const parsed2 = JSON.parse(rawMetadata2);
    assert.strictEqual(parsed2.image, 'https://api.dbd-sigil.org/metadata/2.svg');
  });

  test('Reject unauthorized MetadataPointer updates', async () => {
    await assert.rejects(
      async () => {
        await sigilToken.send('updateMetadataPointer', [user1Hex], user1Hex);
      },
      /Not authorized: pointer authority only/
    );
  });

  test('Lock MetadataPointer permanently', async () => {
    await sigilToken.send('lockMetadataPointer', [], authorityHex);
    assert.strictEqual(await sigilToken.call('isPointerLocked'), true);

    await assert.rejects(
      async () => {
        await sigilToken.send('updateMetadataPointer', [deployerHex], authorityHex);
      },
      /MetadataPointer is locked/
    );

    await assert.rejects(
      async () => {
        await sigilToken.send('lockMetadataPointer', [], authorityHex);
      },
      /MetadataPointer already locked/
    );
  });
});
