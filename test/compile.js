import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';
import solc from 'solc';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

function compileContracts() {
  const contractsDir = path.join(__dirname, '..', 'contracts');
  const files = fs.readdirSync(contractsDir).filter(f => f.endsWith('.sol'));

  const sources = {};
  for (const file of files) {
    sources[file] = {
      content: fs.readFileSync(path.join(contractsDir, file), 'utf8')
    };
  }

  const input = {
    language: 'Solidity',
    sources: sources,
    settings: {
      optimizer: {
        enabled: true,
        runs: 200
      },
      outputSelection: {
        '*': {
          '*': ['abi', 'evm.bytecode']
        }
      }
    }
  };

  function findImports(importPath) {
    const filePath = path.join(contractsDir, importPath);
    if (fs.existsSync(filePath)) {
      return { contents: fs.readFileSync(filePath, 'utf8') };
    }
    return { error: 'File not found: ' + importPath };
  }

  const output = JSON.parse(solc.compile(JSON.stringify(input), { import: findImports }));

  if (output.errors) {
    const fatalErrors = output.errors.filter(e => e.severity === 'error');
    if (fatalErrors.length > 0) {
      console.error('Compilation Errors:', fatalErrors);
      throw new Error('Solidity compilation failed.');
    }
  }

  return output.contracts;
}

export { compileContracts };

if (process.argv[1] === __filename) {
  const contracts = compileContracts();
  console.log('Successfully compiled contracts:', Object.keys(contracts));
}
