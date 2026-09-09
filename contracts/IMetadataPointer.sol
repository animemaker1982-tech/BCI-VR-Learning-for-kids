// SPDX-License-Identifier: MIT
pragma solidity ^0.8.20;

/**
 * @title IMetadataPointer
 * @notice Standard interface for managing a MetadataPointer extension address and authority.
 */
interface IMetadataPointer {
    event MetadataPointerUpdated(address indexed newPointer, address indexed authority);
    event MetadataPointerLocked(address indexed finalPointer);

    /**
     * @notice Returns the current address holding or resolving token metadata.
     */
    function metadataPointer() external view returns (address);

    /**
     * @notice Returns the authority allowed to update or lock the metadata pointer.
     */
    function pointerAuthority() external view returns (address);

    /**
     * @notice Returns true if the metadata pointer is permanently locked.
     */
    function isPointerLocked() external view returns (bool);

    /**
     * @notice Updates the metadata pointer address.
     * @param newPointer The new address pointing to the metadata provider contract.
     */
    function updateMetadataPointer(address newPointer) external;

    /**
     * @notice Permanently locks the metadata pointer address, preventing further updates.
     */
    function lockMetadataPointer() external;
}
