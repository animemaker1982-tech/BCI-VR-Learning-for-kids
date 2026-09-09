// SPDX-License-Identifier: MIT
pragma solidity ^0.8.20;

import "./ISigilMetadata.sol";

/**
 * @title SigilMetadataStore
 * @notice Stores and dynamically generates Sigil metadata and on-chain SVG artwork for Public, Members, and Keepers pathways.
 */
contract SigilMetadataStore is ISigilMetadata {
    address public owner;
    string public baseImageUri;

    mapping(uint256 => Pathway) public tokenPathways;
    mapping(uint256 => uint256) public tokenLevels;

    modifier onlyOwner() {
        require(msg.sender == owner, "Not authorized: owner only");
        _;
    }

    constructor(string memory _baseImageUri) {
        owner = msg.sender;
        baseImageUri = _baseImageUri;
    }

    function setTokenPathwayAndLevel(uint256 tokenId, Pathway pathway, uint256 level) external onlyOwner {
        tokenPathways[tokenId] = pathway;
        tokenLevels[tokenId] = level;
    }

    function setBaseImageUri(string memory _baseImageUri) external onlyOwner {
        baseImageUri = _baseImageUri;
    }

    function getAttributes(uint256 tokenId) public view override returns (SigilAttributes memory) {
        Pathway pathway = tokenPathways[tokenId];
        uint256 level = tokenLevels[tokenId];

        string memory pathwayName = _pathwayToString(pathway);
        string memory name = string(abi.encodePacked("DBD Sigil Token #", _toString(tokenId), " - ", pathwayName));
        string memory symbol = "SIGIL";
        string memory description = string(
            abi.encodePacked("A decentralized Sigil token representing participation in the ", pathwayName, " pathway.")
        );

        string memory imageUri = bytes(baseImageUri).length > 0
            ? string(abi.encodePacked(baseImageUri, "/", _toString(tokenId), ".svg"))
            : getSigilSVG(pathway, level);

        return SigilAttributes({
            name: name,
            symbol: symbol,
            description: description,
            imageUri: imageUri,
            pathway: pathway,
            level: level
        });
    }

    function getMetadata(uint256 tokenId) external view override returns (string memory) {
        SigilAttributes memory attrs = getAttributes(tokenId);
        string memory pathwayStr = _pathwayToString(attrs.pathway);

        string memory part1 = string(abi.encodePacked(
            '{"name":"', attrs.name,
            '","symbol":"', attrs.symbol,
            '","description":"', attrs.description
        ));

        string memory part2 = string(abi.encodePacked(
            '","image":"', attrs.imageUri,
            '","attributes":[{"trait_type":"Pathway","value":"', pathwayStr,
            '"},{"trait_type":"Level","value":', _toString(attrs.level), '}]}'
        ));

        return string(abi.encodePacked(part1, part2));
    }

    function getSigilSVG(Pathway pathway, uint256 level) public pure override returns (string memory) {
        string memory color = "#d6b679";
        if (pathway == Pathway.Members) {
            color = "#7992d6";
        } else if (pathway == Pathway.Keepers) {
            color = "#d67979";
        }

        string memory svgHeader = string(abi.encodePacked(
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 400 400' width='100%' height='100%'>",
            "<rect width='400' height='400' fill='#0d0d12' rx='20'/>",
            "<circle cx='200' cy='200' r='140' fill='none' stroke='", color, "' stroke-width='4'/>"
        ));

        string memory svgBody = string(abi.encodePacked(
            "<polygon points='200,80 230,170 320,200 230,230 200,320 170,230 80,200 170,170' fill='none' stroke='", color, "' stroke-width='3'/>",
            "<circle cx='200' cy='200' r='30' fill='", color, "' opacity='0.8'/>",
            "<text x='200' y='360' font-size='16' fill='#f2efe9' font-family='sans-serif' text-anchor='middle'>Level ", _toString(level), "</text>",
            "</svg>"
        ));

        return string(abi.encodePacked(svgHeader, svgBody));
    }

    function _pathwayToString(Pathway pathway) internal pure returns (string memory) {
        if (pathway == Pathway.Keepers) return "Keepers";
        if (pathway == Pathway.Members) return "Members";
        return "Public";
    }

    function _toString(uint256 value) internal pure returns (string memory) {
        if (value == 0) return "0";
        uint256 temp = value;
        uint256 digits;
        while (temp != 0) {
            digits++;
            temp /= 10;
        }
        bytes memory buffer = new bytes(digits);
        while (value != 0) {
            digits -= 1;
            buffer[digits] = bytes1(uint8(48 + uint256(value % 10)));
            value /= 10;
        }
        return string(buffer);
    }
}
