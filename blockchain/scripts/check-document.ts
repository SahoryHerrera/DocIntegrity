import { network } from "hardhat";

const contractAddress =
  "0x5FbDB2315678afecb367f032d93F642f64180aa3" as `0x${string}`;

const documentHash =
  "0x9405c1b986a75264a68a136ee9d537340556d86e2ee48921e79cf4542756b225" as `0x${string}`;

const { viem } = await network.connect();

const documentRegistry = await viem.getContractAt(
  "DocumentRegistry",
  contractAddress,
);

const registered = await documentRegistry.read.isDocumentRegistered([
  documentHash,
]);

console.log("SHA-256:", documentHash);
console.log("Registrado en blockchain:", registered);
