import { buildModule } from "@nomicfoundation/hardhat-ignition/modules";

const DocumentRegistryModule = buildModule("DocumentRegistryModule", (m) => {
  const documentRegistry = m.contract("DocumentRegistry");

  return { documentRegistry };
});

export default DocumentRegistryModule;