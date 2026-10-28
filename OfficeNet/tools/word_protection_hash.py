"""Word document-protection password hash, written from ECMA-376 Part 4 §2.15.1.28.

An independent implementation used to check WordNet's: it shares no code with the library, so a
test that compares the two catches a mistake in either.

    python tools/word_protection_hash.py <password> <salt-hex> [spin-count]

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.
"""
import base64
import hashlib
import struct
import sys

INITIAL = [0xE1F0, 0x1D0F, 0xCC9C, 0x84C0, 0x110C, 0x0E10, 0xF1CE, 0x313E, 0x1872, 0xE139,
           0xD40F, 0x84F9, 0x280C, 0xA96A, 0x4EC3]
MATRIX = [
    [0xAEFC, 0x4DD9, 0x9BB2, 0x2745, 0x4E8A, 0x9D14, 0x2A09],
    [0x7B61, 0xF6C2, 0xFDA5, 0xEB6B, 0xC6F7, 0x9DCF, 0x2BBF],
    [0x4563, 0x8AC6, 0x05AD, 0x0B5A, 0x16B4, 0x2D68, 0x5AD0],
    [0x0375, 0x06EA, 0x0DD4, 0x1BA8, 0x3750, 0x6EA0, 0xDD40],
    [0xD849, 0xA0B3, 0x5147, 0xA28E, 0x553D, 0xAA7A, 0x44D5],
    [0x6F45, 0xDE8A, 0xAD35, 0x4A4B, 0x9496, 0x390D, 0x721A],
    [0xEB23, 0xC667, 0x9CEF, 0x29FF, 0x53FE, 0xA7FC, 0x5FD9],
    [0x47D3, 0x8FA6, 0x0F6D, 0x1EDA, 0x3DB4, 0x7B68, 0xF6D0],
    [0xB861, 0x60E3, 0xC1C6, 0x93AD, 0x377B, 0x6EF6, 0xDDEC],
    [0x45A0, 0x8B40, 0x06A1, 0x0D42, 0x1A84, 0x3508, 0x6A10],
    [0xAA51, 0x4483, 0x8906, 0x022D, 0x045A, 0x08B4, 0x1168],
    [0x76B4, 0xED68, 0xCAF1, 0x85C3, 0x1BA7, 0x374E, 0x6E9C],
    [0x3730, 0x6E60, 0xDCC0, 0xA9A1, 0x4363, 0x86C6, 0x1DAD],
    [0x3331, 0x6662, 0xCCC4, 0x89A9, 0x0373, 0x06E6, 0x0DCC],
    [0x1021, 0x2042, 0x4084, 0x8108, 0x1231, 0x2462, 0x48C4],
]


def legacy_key(password: str) -> int:
    if not password:
        return 0
    chars = [(ord(c) & 0xFF) or (ord(c) >> 8) for c in password[:15]]
    high = INITIAL[len(chars) - 1]
    for row, ch in enumerate(chars, start=15 - len(chars)):
        for bit in range(7):
            if ch & (1 << bit):
                high ^= MATRIX[row][bit]
    verifier = 0
    for ch in reversed(chars):
        verifier = (((verifier >> 14) & 1) | ((verifier << 1) & 0x7FFF)) ^ ch
    verifier = (((verifier >> 14) & 1) | ((verifier << 1) & 0x7FFF)) ^ len(chars)
    verifier ^= 0xCE4B
    return (high << 16) | verifier


def protection_hash(password: str, salt: bytes, spin: int = 100000) -> bytes:
    key = legacy_key(password)
    hex_reversed = key.to_bytes(4, "little").hex().upper()
    h = hashlib.sha512(salt + hex_reversed.encode("utf-16-le")).digest()
    for i in range(spin):
        h = hashlib.sha512(h + struct.pack("<I", i)).digest()
    return h


if __name__ == "__main__":
    if len(sys.argv) == 1:
        assert legacy_key("Example") == 0x64CEED7E, hex(legacy_key("Example"))
        print("spec example ok")
    else:
        spin = int(sys.argv[3]) if len(sys.argv) > 3 else 100000
        print(base64.b64encode(protection_hash(sys.argv[1], bytes.fromhex(sys.argv[2]), spin)).decode())
