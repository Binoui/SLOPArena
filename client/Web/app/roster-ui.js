// Keep descriptions and WIP tags aligned with Runtime/UI/MenuRoster.cs.
export const ROSTER_UI = {
  fightguy: { description: 'Loves punching stuff. But also kicking stuff.' },
  manki: { description: "There's nothing a few explosives can't fix." },
  wibou: { description: 'He studied the blade. A lot.' },
  bonk: { description: 'Sir Bonk do bonky bonks.' },
  bashie: { description: 'NO RUNNING! (She speed-walks.)', wip: true },
  salamander: { description: 'The fastest fingers in the West.', wip: true },
  nyx: { description: 'A witch with another side.', wip: true },
};
export const portrait = (assets, pkg, form = 0) => `${assets}ui/portraits/${pkg}${pkg === 'nyx' && form === 1 ? '_fake' : ''}.webp`;
