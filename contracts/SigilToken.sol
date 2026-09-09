// SPDX-License-Identifier: MIT
pragma solidity ^0.8.20;

import "./IMetadataPointer.sol";
import "./ISigilMetadata.sol";

/**
 * @title SigilToken
 * @notice Cryptocurrency token contract incorporating the MetadataPointer extension standard.
 */
contract SigilToken is IMetadataPointer {
    string public name;
    string public symbol;
    uint8 public immutable decimals = 18;
    uint256 public totalSupply;

    mapping(address => uint256) public balanceOf;
    mapping(address => mapping(address => uint256)) public allowance;

    // MetadataPointer State
    address private _metadataPointer;
    address private _pointerAuthority;
    bool private _pointerLocked;

    event Transfer(address indexed from, address indexed to, uint256 value);
    event Approval(address indexed owner, address indexed spender, uint256 value);

    modifier onlyPointerAuthority() {
        require(msg.sender == _pointerAuthority, "Not authorized: pointer authority only");
        _;
    }

    constructor(
        string memory _name,
        string memory _symbol,
        uint256 initialSupply,
        address initialMetadataPointer,
        address initialPointerAuthority
    ) {
        name = _name;
        symbol = _symbol;

        _mint(msg.sender, initialSupply);

        _metadataPointer = initialMetadataPointer;
        _pointerAuthority = initialPointerAuthority;
        _pointerLocked = false;

        emit MetadataPointerUpdated(initialMetadataPointer, initialPointerAuthority);
    }

    // --- IMetadataPointer Implementation ---

    function metadataPointer() external view override returns (address) {
        return _metadataPointer;
    }

    function pointerAuthority() external view override returns (address) {
        return _pointerAuthority;
    }

    function isPointerLocked() external view override returns (bool) {
        return _pointerLocked;
    }

    function updateMetadataPointer(address newPointer) external override onlyPointerAuthority {
        require(!_pointerLocked, "MetadataPointer is locked");
        require(newPointer != address(0), "Invalid pointer address");
        _metadataPointer = newPointer;
        emit MetadataPointerUpdated(newPointer, msg.sender);
    }

    function lockMetadataPointer() external override onlyPointerAuthority {
        require(!_pointerLocked, "MetadataPointer already locked");
        _pointerLocked = true;
        emit MetadataPointerLocked(_metadataPointer);
    }

    function setPointerAuthority(address newAuthority) external onlyPointerAuthority {
        require(newAuthority != address(0), "Invalid authority address");
        _pointerAuthority = newAuthority;
    }

    // --- Metadata Resolution ---

    function tokenURI(uint256 tokenId) external view returns (string memory) {
        require(_metadataPointer != address(0), "MetadataPointer not configured");
        return ISigilMetadata(_metadataPointer).getMetadata(tokenId);
    }

    function getSigilAttributes(uint256 tokenId) external view returns (ISigilMetadata.SigilAttributes memory) {
        require(_metadataPointer != address(0), "MetadataPointer not configured");
        return ISigilMetadata(_metadataPointer).getAttributes(tokenId);
    }

    // --- Basic Token Logic ---

    function transfer(address to, uint256 amount) external returns (bool) {
        _transfer(msg.sender, to, amount);
        return true;
    }

    function approve(address spender, uint256 amount) external returns (bool) {
        allowance[msg.sender][spender] = amount;
        emit Approval(msg.sender, spender, amount);
        return true;
    }

    function transferFrom(address from, address to, uint256 amount) external returns (bool) {
        uint256 currentAllowance = allowance[from][msg.sender];
        if (currentAllowance != type(uint256).max) {
            require(currentAllowance >= amount, "ERC20: insufficient allowance");
            unchecked {
                allowance[from][msg.sender] = currentAllowance - amount;
            }
        }
        _transfer(from, to, amount);
        return true;
    }

    function _transfer(address from, address to, uint256 amount) internal {
        require(from != address(0), "ERC20: transfer from zero address");
        require(to != address(0), "ERC20: transfer to zero address");
        require(balanceOf[from] >= amount, "ERC20: transfer amount exceeds balance");

        unchecked {
            balanceOf[from] -= amount;
            balanceOf[to] += amount;
        }

        emit Transfer(from, to, amount);
    }

    function _mint(address account, uint256 amount) internal {
        require(account != address(0), "ERC20: mint to zero address");
        totalSupply += amount;
        unchecked {
            balanceOf[account] += amount;
        }
        emit Transfer(address(0), account, amount);
    }
}
