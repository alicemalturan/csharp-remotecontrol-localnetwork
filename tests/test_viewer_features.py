import unittest
from pathlib import Path

APP = Path('viewer/app.js').read_text(encoding='utf-8')
HTML = Path('viewer/index.html').read_text(encoding='utf-8')

class ViewerFeatureSmokeTests(unittest.TestCase):
    def test_required_controls_exist(self):
        for token in ['OrbitControls', 'controls.autoRotate', 'data-view="front"', 'toggleMeasure', 'toggleSection', 'arButton']:
            self.assertIn(token, APP + HTML)

    def test_supported_format_loaders(self):
        for loader in ['GLTFLoader', 'OBJLoader', 'STLLoader', 'DRACOLoader', 'MeshoptDecoder']:
            self.assertIn(loader, APP)

    def test_quality_features(self):
        for token in ['MeshStandardMaterial', 'scene.environment', 'DirectionalLight', 'clippingPlanes']:
            self.assertIn(token, APP)

if __name__ == '__main__':
    unittest.main()
