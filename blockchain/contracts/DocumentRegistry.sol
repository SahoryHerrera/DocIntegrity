// SPDX-License-Identifier: MIT
pragma solidity ^0.8.28;

contract DocumentRegistry
{
    struct DocumentRecord
    {
        bytes32 documentHash;
        uint256 registeredAt;
        address registeredBy;
        bool exists;
    }

    mapping(bytes32 => DocumentRecord) private documents;

    event DocumentRegistered(
        bytes32 indexed documentHash,
        uint256 registeredAt,
        address indexed registeredBy
    );

    function registerDocument(bytes32 documentHash) external
    {
        require(
            !documents[documentHash].exists,
            "Document already registered"
        );

        documents[documentHash] = DocumentRecord({
            documentHash: documentHash,
            registeredAt: block.timestamp,
            registeredBy: msg.sender,
            exists: true
        });

        emit DocumentRegistered(
            documentHash,
            block.timestamp,
            msg.sender
        );
    }

    function getDocument(bytes32 documentHash)
        external
        view
        returns (
            bytes32,
            uint256,
            address,
            bool
        )
    {
        DocumentRecord memory document = documents[documentHash];

        return (
            document.documentHash,
            document.registeredAt,
            document.registeredBy,
            document.exists
        );
    }

    function isDocumentRegistered(bytes32 documentHash)
        external
        view
        returns (bool)
    {
        return documents[documentHash].exists;
    }
}