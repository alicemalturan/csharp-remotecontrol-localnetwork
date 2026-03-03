import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { OBJLoader } from 'three/addons/loaders/OBJLoader.js';
import { STLLoader } from 'three/addons/loaders/STLLoader.js';
import { DRACOLoader } from 'three/addons/loaders/DRACOLoader.js';
import { RGBELoader } from 'three/addons/loaders/RGBELoader.js';
import { MeshoptDecoder } from 'three/addons/libs/meshopt_decoder.module.js';
import { ARButton } from 'three/addons/webxr/ARButton.js';

const viewport = document.getElementById('viewport');
const scene = new THREE.Scene();
const camera = new THREE.PerspectiveCamera(55, 1, 0.1, 200);
camera.position.set(2.5, 1.8, 2.5);

const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setSize(viewport.clientWidth, viewport.clientHeight);
renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
renderer.shadowMap.enabled = true;
renderer.localClippingEnabled = true;
renderer.xr.enabled = true;
viewport.appendChild(renderer.domElement);

const controls = new OrbitControls(camera, renderer.domElement);
controls.enableDamping = true;
controls.autoRotate = false;
controls.autoRotateSpeed = 0.7;

const hemi = new THREE.HemisphereLight(0xffffff, 0x203040, 1);
scene.add(hemi);
const dir = new THREE.DirectionalLight(0xffffff, 1.1);
dir.position.set(4, 7, 2);
dir.castShadow = true;
scene.add(dir);
scene.add(new THREE.GridHelper(10, 10, 0x2f4672, 0x1f2a4b));

const manager = new THREE.LoadingManager();
const loadingOverlay = document.getElementById('loadingOverlay');
const loadingText = document.getElementById('loadingText');
manager.onProgress = (_, loaded, total) => loadingText.textContent = `Model yükleniyor... %${Math.round((loaded/Math.max(total,1))*100)}`;
manager.onLoad = () => loadingOverlay.style.display = 'none';

const gltfLoader = new GLTFLoader(manager);
const draco = new DRACOLoader();
draco.setDecoderPath('https://unpkg.com/three@0.164.1/examples/jsm/libs/draco/');
gltfLoader.setDRACOLoader(draco);
gltfLoader.setMeshoptDecoder(MeshoptDecoder);
const objLoader = new OBJLoader(manager);
const stlLoader = new STLLoader(manager);

let modelRoot = null, mixer = null, activeAction = null, clock = new THREE.Clock();
let sectionEnabled = false;
const sectionPlane = new THREE.Plane(new THREE.Vector3(0, -1, 0), 0);
let measureMode = false;
let hotspotMode = false;
const picks = [];
const raycaster = new THREE.Raycaster();
const pointer = new THREE.Vector2();

function setModel(root) {
  if (modelRoot) scene.remove(modelRoot);
  modelRoot = root;
  modelRoot.traverse((o) => {
    if (o.isMesh) {
      o.castShadow = true;
      o.receiveShadow = true;
      if (!(o.material instanceof THREE.MeshStandardMaterial)) {
        o.material = new THREE.MeshStandardMaterial({ color: o.material?.color || 0xcccccc, metalness: 0.5, roughness: 0.45 });
      }
    }
  });
  scene.add(modelRoot);
  fitCameraToObject(modelRoot);
}

function fitCameraToObject(object) {
  const box = new THREE.Box3().setFromObject(object);
  const size = box.getSize(new THREE.Vector3()).length();
  const center = box.getCenter(new THREE.Vector3());
  controls.target.copy(center);
  camera.position.copy(center).add(new THREE.Vector3(size * 0.6, size * 0.35, size * 0.6));
  camera.near = size / 100;
  camera.far = size * 100;
  camera.updateProjectionMatrix();
}

async function loadEnv(name) {
  const map = {
    studio: 'https://dl.polyhaven.org/file/ph-assets/HDRIs/hdr/1k/studio_small_03_1k.hdr',
    city: 'https://dl.polyhaven.org/file/ph-assets/HDRIs/hdr/1k/qwantani_1k.hdr',
    sunset: 'https://dl.polyhaven.org/file/ph-assets/HDRIs/hdr/1k/rosendal_park_sunset_1k.hdr'
  };
  const tex = await new RGBELoader(manager).loadAsync(map[name]);
  tex.mapping = THREE.EquirectangularReflectionMapping;
  scene.environment = tex;
  scene.background = tex;
}

async function loadModelFromUrl(url) {
  loadingOverlay.style.display = 'flex';
  const lower = url.toLowerCase();
  if (lower.endsWith('.glb') || lower.endsWith('.gltf')) {
    const gltf = await gltfLoader.loadAsync(url);
    setModel(gltf.scene);
    if (gltf.animations?.length) {
      mixer = new THREE.AnimationMixer(gltf.scene);
      activeAction = mixer.clipAction(gltf.animations[0]);
      activeAction.play();
    }
    document.getElementById('sceneViewerLink').href = `intent://arvr.google.com/scene-viewer/1.0?file=${encodeURIComponent(url)}&mode=ar_preferred#Intent;scheme=https;package=com.google.ar.core;end;`;
  } else if (lower.endsWith('.obj')) {
    setModel(await objLoader.loadAsync(url));
  } else if (lower.endsWith('.stl')) {
    const geo = await stlLoader.loadAsync(url);
    setModel(new THREE.Mesh(geo, new THREE.MeshStandardMaterial({ color: 0xb6d1ff, metalness: 0.1, roughness: 0.7 })));
  } else {
    alert('Desteklenmeyen format');
  }
}

function loadFromFile(file) {
  const url = URL.createObjectURL(file);
  loadModelFromUrl(url);
}

function moveCameraView(view) {
  if (!modelRoot) return;
  const c = controls.target.clone();
  const d = camera.position.distanceTo(c);
  const map = { front:[0,0,1], back:[0,0,-1], left:[-1,0,0], right:[1,0,0], top:[0,1,0] };
  const v = map[view];
  camera.position.set(c.x + v[0]*d, c.y + Math.max(0.2, v[1]*d), c.z + v[2]*d);
}

renderer.domElement.addEventListener('click', (ev) => {
  if (!modelRoot || (!measureMode && !hotspotMode)) return;
  const rect = renderer.domElement.getBoundingClientRect();
  pointer.x = ((ev.clientX - rect.left) / rect.width) * 2 - 1;
  pointer.y = -((ev.clientY - rect.top) / rect.height) * 2 + 1;
  raycaster.setFromCamera(pointer, camera);
  const hit = raycaster.intersectObject(modelRoot, true)[0];
  if (!hit) return;

  if (measureMode) {
    picks.push(hit.point.clone());
    if (picks.length === 2) {
      const dist = picks[0].distanceTo(picks[1]);
      document.getElementById('measureResult').textContent = `Mesafe: ${dist.toFixed(3)} m`;
      const line = new THREE.Line(new THREE.BufferGeometry().setFromPoints(picks), new THREE.LineBasicMaterial({ color: 0xffe66d }));
      scene.add(line); picks.length = 0;
    }
  }

  if (hotspotMode) {
    const label = prompt('Hotspot metni?');
    if (label) {
      const el = document.createElement('div');
      el.className = 'hotspot';
      el.textContent = label;
      document.body.appendChild(el);
      el._point = hit.point.clone();
      hotspots.push(el);
    }
    hotspotMode = false;
  }
});

const hotspots = [];
function updateHotspots() {
  hotspots.forEach((el) => {
    const p = el._point.clone().project(camera);
    el.style.left = `${(p.x * 0.5 + 0.5) * window.innerWidth}px`;
    el.style.top = `${(-p.y * 0.5 + 0.5) * window.innerHeight}px`;
    el.style.display = p.z > 1 ? 'none' : 'block';
  });
}

window.addEventListener('resize', () => {
  camera.aspect = viewport.clientWidth / viewport.clientHeight;
  camera.updateProjectionMatrix();
  renderer.setSize(viewport.clientWidth, viewport.clientHeight);
});

// UI bindings
document.getElementById('btnLoadUrl').onclick = () => loadModelFromUrl(document.getElementById('modelUrl').value.trim());
document.getElementById('fileInput').onchange = (e) => e.target.files[0] && loadFromFile(e.target.files[0]);
document.querySelectorAll('[data-view]').forEach((b) => b.onclick = () => moveCameraView(b.dataset.view));
document.getElementById('toggleAutoRotate').onclick = () => controls.autoRotate = !controls.autoRotate;
document.getElementById('ambientIntensity').oninput = (e) => hemi.intensity = Number(e.target.value);
document.getElementById('shadowIntensity').oninput = (e) => dir.intensity = 0.1 + Number(e.target.value) * 2;
document.getElementById('envPreset').onchange = (e) => loadEnv(e.target.value);
document.getElementById('applyPbrVariant').onclick = () => {
  if (!modelRoot) return;
  const color = new THREE.Color().setHSL(Math.random(), 0.4, 0.55);
  modelRoot.traverse((o) => o.isMesh && o.material && o.material.color && o.material.color.copy(color));
};
document.getElementById('toggleMeasure').onclick = () => measureMode = !measureMode;
document.getElementById('sectionSlider').oninput = (e) => sectionPlane.constant = Number(e.target.value);
document.getElementById('toggleSection').onclick = () => {
  sectionEnabled = !sectionEnabled;
  renderer.clippingPlanes = sectionEnabled ? [sectionPlane] : [];
};
document.getElementById('animPlay').onclick = () => activeAction && (activeAction.paused = false);
document.getElementById('animPause').onclick = () => activeAction && (activeAction.paused = true);
document.getElementById('animFaster').onclick = () => mixer && (mixer.timeScale = mixer.timeScale === 1 ? 2 : 1);
document.getElementById('addHotspot').onclick = () => hotspotMode = true;
document.getElementById('arButton').onclick = () => document.body.appendChild(ARButton.createButton(renderer, { requiredFeatures: ['hit-test'] }));

loadEnv('studio');
loadModelFromUrl(document.getElementById('modelUrl').value);

renderer.setAnimationLoop(() => {
  controls.update();
  if (mixer) mixer.update(clock.getDelta());
  updateHotspots();
  renderer.render(scene, camera);
});
