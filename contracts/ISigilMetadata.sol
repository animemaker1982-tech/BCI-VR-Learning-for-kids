// SPDX-License-Identifier: MIT
pragma solidity ^0.8.20;

/**
 * @title ISigilMetadata
 * @notice Interface for resolving token metadata and rendering dynamic Sigil SVG graphics.
 */
interface ISigilMetadata {
    enum Pathway { Public, Members, Keepers }

    struct SigilAttributes {
        string name;
        string symbol;
        string description;
        string imageUri;
        Pathway pathway;
        uint256 level;
    }

    /**
     * @notice Returns the metadata URI or JSON data string for a given token ID.
     */
    function getMetadata(uint256 tokenId) external view returns (string memory jsonUri);

    /**
     * @notice Returns structured Sigil attributes for a given token ID.
     */
    function getAttributes(uint256 tokenId) external view returns (SigilAttributes memory);

    /**
     * @notice Renders an SVG string representing the Sigil for a given pathway and level.
     */
    function getSigilSVG(Pathway pathway, uint256 level) external view returns (string memory svg);
}
