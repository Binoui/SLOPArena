import * as THREE from 'three';

// Shared is left-handed (Unity). Scene objects live under a world group that mirrors Z;
// toThree converts a sim point for code that works outside that group (camera, screen projection).
export const toThree = (x, y, z) => new THREE.Vector3(x, y, -z);

export const UP = new THREE.Vector3(0, 1, 0);
export const unitSphere = new THREE.SphereGeometry(1, 20, 14);
export const unitCylinder = new THREE.CylinderGeometry(1, 1, 1, 16, 1);

export function placeSegment(mesh, a, b, r) {
  const d = new THREE.Vector3().subVectors(b, a);
  const len = Math.max(d.length(), 1e-4);
  mesh.position.copy(a).addScaledVector(d, 0.5);
  mesh.quaternion.setFromUnitVectors(UP, d.divideScalar(len));
  mesh.scale.set(r, len, r);
}

export function placeBall(mesh, p, r) {
  mesh.position.copy(p);
  mesh.scale.setScalar(r);
}

// The six semantic bones content attaches effects to, and the cooked pose point that carries each.
export const SEMANTIC_BONES = {
  'bone.hips': ':Hips',
  'bone.head': ':Head',
  'bone.right-hand': ':RightHand',
  'bone.left-hand': ':LeftHand',
  'bone.right-foot': ':RightFoot',
  'bone.left-foot': ':LeftFoot',
};
