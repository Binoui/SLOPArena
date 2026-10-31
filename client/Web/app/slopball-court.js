import * as THREE from 'three';
import { unitSphere, unitCylinder, placeSegment, placeBall } from './geometry.js';

export function createSlopballCourt({ world, court, stageArt }) {
  const props = new THREE.Group();
  // The exported Night Court carries its own net; the prototype one stands in on the grey box.
  const ownNet = !!stageArt?.getObjectByName('Court Net');
  const width = court.landing[0] * 2 + 3;
  const net = new THREE.Mesh(new THREE.BoxGeometry(width, court.netTop - court.floorY, Math.max(court.netHalf * 2, 0.08)),
    new THREE.MeshStandardMaterial({ color: 0xf4efe2, transparent: true, opacity: 0.55, roughness: 0.9 }));
  net.position.set(0, (court.floorY + court.netTop) / 2, court.netZ);
  const tape = new THREE.Mesh(new THREE.BoxGeometry(width, 0.08, 0.1), new THREE.MeshStandardMaterial({ color: 0xffcf3a }));
  tape.position.set(0, court.netTop, court.netZ);
  for (const x of [-width / 2, width / 2]) {
    const post = new THREE.Mesh(unitCylinder, new THREE.MeshStandardMaterial({ color: 0x2b2438 }));
    placeSegment(post, new THREE.Vector3(x, court.floorY, court.netZ), new THREE.Vector3(x, court.netTop + 0.1, court.netZ), 0.07);
    props.add(post);
  }
  const launcher = new THREE.Group();
  const body = new THREE.Mesh(new THREE.CylinderGeometry(0.7, 0.9, 1, 20), new THREE.MeshStandardMaterial({ color: 0x3a3150, roughness: 0.6 }));
  body.position.y = 0.5;
  const barrel = new THREE.Mesh(new THREE.CylinderGeometry(0.32, 0.4, 1.3, 16), new THREE.MeshStandardMaterial({ color: 0xff6b3d, roughness: 0.4 }));
  barrel.position.set(0, 1.2, -0.35);
  barrel.rotation.x = -0.9;
  launcher.add(body, barrel);
  const ball = new THREE.Mesh(unitSphere, new THREE.MeshStandardMaterial({ color: 0xffd23a, emissive: 0xff7a00, emissiveIntensity: 0.4, roughness: 0.35 }));
  ball.castShadow = true;
  const marker = new THREE.Mesh(new THREE.RingGeometry(0.7, 0.85, 32), new THREE.MeshBasicMaterial({ color: 0xffd23a, transparent: true, opacity: 0.8, side: THREE.DoubleSide }));
  marker.rotation.x = -Math.PI / 2;
  const drop = new THREE.Mesh(unitCylinder, new THREE.MeshBasicMaterial({ color: 0xffd23a, transparent: true, opacity: 0.25 }));
  if (ownNet) props.children.slice().forEach(post => props.remove(post));
  else props.add(net, tape);
  props.add(launcher, ball, marker, drop);
  world.add(props);
  return {
    draw(s) {
      const b = s.ball, c = court;
      launcher.position.set(s.launcher[0], c.floorY, s.launcher[2]);
      for (const m of [ball, marker, drop]) m.visible = b.active;
      if (!b.active) return;
      placeBall(ball, new THREE.Vector3(...b.p), b.r);
      ball.material.emissiveIntensity = 0.3 + s.heat * 0.3;
      marker.position.set(b.p[0], c.floorY + 0.02, b.p[2]);
      marker.scale.setScalar(Math.max(0.4, b.r * 1.6));
      placeSegment(drop, new THREE.Vector3(b.p[0], c.floorY, b.p[2]), new THREE.Vector3(...b.p), 0.03);
    },
    dispose() { world.remove(props); },
  };
}
