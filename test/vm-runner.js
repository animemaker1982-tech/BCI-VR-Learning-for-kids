import { createVM } from '@ethereumjs/vm';
import { Address, hexToBytes, bytesToHex } from '@ethereumjs/util';
import { ethers } from 'ethers';
import { compileContracts } from './compile.js';

export async function createEVMRunner() {
  const vm = await createVM();
  const compiled = compileContracts();

  // Helper to deploy contract
  async function deployContract(contractFile, contractName, constructorArgs = [], callerAddressHex = '0x1000000000000000000000000000000000000001') {
    const artifact = compiled[contractFile][contractName];
    const abi = artifact.abi;
    const bytecode = artifact.evm.bytecode.object;

    const iface = new ethers.Interface(abi);
    const encodedArgs = constructorArgs.length > 0 ? iface.encodeDeploy(constructorArgs).slice(2) : '';
    const deployBytecode = '0x' + bytecode + encodedArgs;

    const caller = Address.fromString(callerAddressHex);

    const res = await vm.evm.runCall({
      caller,
      data: hexToBytes(deployBytecode),
      gasLimit: 10000000n,
    });

    if (res.execResult.exceptionError) {
      throw new Error(`Deployment failed: ${res.execResult.exceptionError.error}`);
    }

    const createdAddressHex = res.createdAddress.toString();

    // Return helper object to call contract methods
    return {
      address: createdAddressHex,
      abi: abi,
      interface: iface,
      async call(methodName, args = [], callerHex = callerAddressHex) {
        const dataHex = iface.encodeFunctionData(methodName, args);
        const result = await vm.evm.runCall({
          caller: Address.fromString(callerHex),
          to: res.createdAddress,
          data: hexToBytes(dataHex),
          gasLimit: 10000000n,
        });

        if (result.execResult.exceptionError) {
          throw new Error(`Call to ${methodName} reverted: ${result.execResult.exceptionError.error}`);
        }

        const returnHex = bytesToHex(result.execResult.returnValue);
        if (returnHex === '0x' || returnHex === '') {
          return null;
        }
        const decoded = iface.decodeFunctionResult(methodName, returnHex);
        return decoded.length === 1 ? decoded[0] : decoded;
      },
      async send(methodName, args = [], callerHex = callerAddressHex) {
        const dataHex = iface.encodeFunctionData(methodName, args);
        const result = await vm.evm.runCall({
          caller: Address.fromString(callerHex),
          to: res.createdAddress,
          data: hexToBytes(dataHex),
          gasLimit: 10000000n,
        });

        if (result.execResult.exceptionError) {
          // Attempt revert message decode
          const returnHex = bytesToHex(result.execResult.returnValue);
          if (returnHex.startsWith('0x08c379a0')) { // Error(string)
            const reason = ethers.AbiCoder.defaultAbiCoder().decode(['string'], '0x' + returnHex.slice(10))[0];
            throw new Error(`Reverted: ${reason}`);
          }
          throw new Error(`Execution reverted: ${result.execResult.exceptionError.error}`);
        }

        return result;
      }
    };
  }

  return { vm, deployContract };
}
