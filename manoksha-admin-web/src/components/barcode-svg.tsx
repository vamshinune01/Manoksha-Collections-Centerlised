/**
 * Renders a scannable barcode as SVG: EAN-13 for 13-digit numeric codes, Code 128 (set B) otherwise.
 * Pure presentation — the code itself always comes from the backend.
 */
const EAN_L = ["0001101", "0011001", "0010011", "0111101", "0100011", "0110001", "0101111", "0111011", "0110111", "0001011"];
const EAN_G = ["0100111", "0110011", "0011011", "0100001", "0011101", "0111001", "0000101", "0010001", "0001001", "0010111"];
const EAN_R = ["1110010", "1100110", "1101100", "1000010", "1011100", "1001110", "1010000", "1000100", "1001000", "1110100"];
const EAN_PARITY = ["LLLLLL", "LLGLGG", "LLGGLG", "LLGGGL", "LGLLGG", "LGGLLG", "LGGGLL", "LGLGLG", "LGLGGL", "LGGLGL"];

// Code 128 bar/space widths for values 0–106 (each entry: 6 digits, stop has 7).
const C128 = [
  "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213", "221312", "231212", "112232", "122132", "122231",
  "113222", "123122", "123221", "223211", "221132", "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
  "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313", "231113", "231311", "112133", "112331", "132131",
  "113123", "113321", "133121", "313121", "211331", "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
  "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214", "112412", "122114", "122411", "142112", "142211",
  "241211", "221114", "413111", "241112", "134111", "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
  "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141", "114131", "311141", "411131", "211412", "211214",
  "211232", "2331112",
];

function ean13Modules(code: string): string {
  const first = Number(code[0]);
  const parity = EAN_PARITY[first]!;
  let bits = "101";
  for (let i = 1; i <= 6; i++) bits += (parity[i - 1] === "L" ? EAN_L : EAN_G)[Number(code[i])];
  bits += "01010";
  for (let i = 7; i <= 12; i++) bits += EAN_R[Number(code[i])];
  return bits + "101";
}

function code128Modules(code: string): string {
  const values = [104, ...[...code].map((c) => c.charCodeAt(0) - 32)];
  const checksum = values.reduce((sum, v, i) => sum + v * (i === 0 ? 1 : i), 0) % 103;
  let bits = "";
  for (const v of [...values, checksum, 106]) {
    const widths = C128[v]!;
    [...widths].forEach((w, i) => (bits += (i % 2 === 0 ? "1" : "0").repeat(Number(w))));
  }
  return bits;
}

export function BarcodeSvg({ code, height = 48, moduleWidth = 1.6 }: { code: string; height?: number; moduleWidth?: number }) {
  const isEan = /^\d{13}$/.test(code);
  const bits = isEan ? ean13Modules(code) : code128Modules(code);
  const quiet = 10;
  const width = (bits.length + quiet * 2) * moduleWidth;
  const bars: { x: number; w: number }[] = [];
  for (let i = 0; i < bits.length; ) {
    if (bits[i] === "1") {
      let j = i;
      while (bits[j] === "1") j++;
      bars.push({ x: (i + quiet) * moduleWidth, w: (j - i) * moduleWidth });
      i = j;
    } else i++;
  }
  return (
    <svg viewBox={`0 0 ${width} ${height + 14}`} width={width} height={height + 14} role="img" aria-label={`Barcode ${code}`}>
      <rect width={width} height={height + 14} fill="white" />
      {bars.map((b) => (
        <rect key={b.x} x={b.x} y={0} width={b.w} height={height} fill="black" />
      ))}
      <text x={width / 2} y={height + 12} textAnchor="middle" fontSize="11" fontFamily="monospace" fill="black">
        {code}
      </text>
    </svg>
  );
}
