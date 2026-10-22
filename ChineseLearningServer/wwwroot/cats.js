// The mochi cats of the desktop app (CatArt.cs), as SVG: a soft grey or white
// bean of a cat with dot eyes, rosy cheeks and a tiny ω mouth. Drawn in a
// 100 × 100 box; CSS animates them (breathing, a little hop on hover).

const fur = { grey: '#9e9594', white: '#fffdfa' };
const line = '#70605c';

const body = 'M16 30 L20 11 L36 20 C45 16 55 16 64 20 L80 11 L84 30 C95 42 94 58 83 64 '
  + 'C88 74 85 91 74 96 C60 99 40 99 26 96 C15 91 12 74 17 64 C6 58 5 42 16 30 Z';

function eyes(mood) {
  if (mood === 'happy') {
    return `<path d="M33 49 Q37 44 41 49 M59 49 Q63 44 67 49" stroke="${line}" stroke-width="2.4" fill="none" stroke-linecap="round"/>`;
  }
  if (mood === 'sleepy') {
    return `<path d="M33 46 Q37 50 41 46 M59 46 Q63 50 67 46" stroke="${line}" stroke-width="2.4" fill="none" stroke-linecap="round"/>`;
  }
  const r = mood === 'surprised' ? 3.8 : 3.1;
  return [37, 63].map(x =>
    `<ellipse class="eye" cx="${x}" cy="48" rx="${r}" ry="${r * 1.1}" fill="#403432"/>`
    + `<circle cx="${x - 1}" cy="46.6" r="${r * 0.36}" fill="#fff"/>`).join('');
}

function mouth(mood) {
  if (mood === 'sad') {
    return `<path d="M47 56 Q50 53 53 56" stroke="${line}" stroke-width="2.2" fill="none" stroke-linecap="round"/>`;
  }
  if (mood === 'surprised') {
    return `<ellipse cx="50" cy="54.5" rx="2" ry="2.3" fill="none" stroke="${line}" stroke-width="2"/>`;
  }
  return `<path d="M45.5 52 Q47.75 56 50 52 Q52.25 56 54.5 52" stroke="${line}" stroke-width="2.2" fill="none" stroke-linecap="round"/>`;
}

// coat: 'grey' | 'white'; mood: 'smile' | 'happy' | 'sleepy' | 'surprised' | 'sad';
// tail: 1 (right) or -1 (left).
export function mochiCat({ coat = 'white', mood = 'smile', tail = 1, title = '' } = {}) {
  const tailPath = tail >= 0
    ? 'M76 90 C96 94 100 76 91 68'
    : 'M24 90 C4 94 0 76 9 68';
  return `<svg class="cat" viewBox="-2 0 104 100" role="img" aria-label="${title}">
    <path d="${tailPath}" stroke="${line}" stroke-width="10" fill="none" stroke-linecap="round"/>
    <path d="${tailPath}" stroke="${fur[coat]}" stroke-width="4.4" fill="none" stroke-linecap="round"/>
    <path d="${body}" fill="${fur[coat]}" stroke="${line}" stroke-width="2.8" stroke-linejoin="round"/>
    ${coat === 'grey' ? '<ellipse cx="50" cy="79" rx="20" ry="13" fill="#fff" opacity=".27"/>' : ''}
    <path d="M21 16 L21 27 L31 22 Z M79 16 L79 27 L69 22 Z" fill="#f4a8ac"/>
    <ellipse cx="25.5" cy="55" rx="6.5" ry="3.3" fill="#fa96a2" opacity=".67"/>
    <ellipse cx="74.5" cy="55" rx="6.5" ry="3.3" fill="#fa96a2" opacity=".67"/>
    ${eyes(mood)}${mouth(mood)}
    <ellipse cx="39" cy="80" rx="6.5" ry="5.5" fill="${fur[coat]}" stroke="${line}" stroke-width="2.8"/>
    <ellipse cx="61" cy="80" rx="6.5" ry="5.5" fill="${fur[coat]}" stroke="${line}" stroke-width="2.8"/>
    <path d="M43 97 L44 90 M57 97 L56 90" stroke="${line}" stroke-width="2.8" stroke-linecap="round"/>
  </svg>`;
}
