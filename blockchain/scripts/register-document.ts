import { network } from "hardhat";

const contractAddress =
  "0x5FbDB2315678afecb367f032d93F642f64180aa3" as `0x${string}`;

const documentHash =
  "0xc4c9bc417918e273d02fec056def02db75a63ee263495d2e580c80261ecaf90b" as `0x${string}`;

const { viem } = await network.connect();

const documentRegistry = await viem.getContractAt(
  "DocumentRegistry",
  contractAddress
);

console.log("Registrando documento...");
console.log("SHA-256:", documentHash);

const transactionHash =
  await documentRegistry.write.registerDocument([documentHash]);

console.log("Transaction Hash:", transactionHash);

const publicClient = await viem.getPublicClient();

const receipt = await publicClient.waitForTransactionReceipt({
  hash: transactionHash
});

console.log("Block Number:", receipt.blockNumber.toString());

const isRegistered =
  await documentRegistry.read.isDocumentRegistered([documentHash]);

console.log("Documento registrado:", isRegistered);