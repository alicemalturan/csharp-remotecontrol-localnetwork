import hmac, hashlib, struct, unittest

MAGIC = 0x50494E31

def build_auth(pin: str):
    data = struct.pack('<i', MAGIC)
    enc = pin.encode('utf-8')
    # BinaryWriter string is 7-bit length, emulate simple single-byte for short values
    data += bytes([len(enc)]) + enc
    return data

def sign_payload(pin: str, payload: bytes):
    key = hashlib.sha256(pin.encode('utf-8')).digest()
    sig = hmac.new(key, payload, hashlib.sha256).digest()
    return sig


def is_allowed(ip: str, allow_list: str):
    entries = [x.strip() for x in allow_list.split(',') if x.strip()]
    if not entries:
        return True
    for e in entries:
        if e == ip:
            return True
        if e.endswith('/24'):
            base = e[:-3]
            if '.' in base and ip.startswith(base[:base.rfind('.') + 1]):
                return True
    return False


class ProtocolTests(unittest.TestCase):
    def test_auth_magic(self):
        packet = build_auth('123456')
        self.assertEqual(struct.unpack('<i', packet[:4])[0], MAGIC)

    def test_hmac_signature_changes(self):
        s1 = sign_payload('111111', b'abc')
        s2 = sign_payload('222222', b'abc')
        self.assertNotEqual(s1, s2)

    def test_allow_list(self):
        self.assertTrue(is_allowed('192.168.1.42', '192.168.1.0/24'))
        self.assertFalse(is_allowed('10.0.0.2', '192.168.1.0/24'))
        self.assertTrue(is_allowed('10.0.0.2', '10.0.0.2'))

if __name__ == '__main__':
    unittest.main()
