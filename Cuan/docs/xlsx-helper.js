/**
 * Pembantu untuk test-import.js: mengisi baris data ke dalam template .xlsx.
 *
 * Penulisan .xlsx-nya didelegasikan ke openpyxl lewat Python, supaya uji ini
 * tidak menambah ketergantungan npm hanya untuk menulis satu berkas.
 */
const { execFileSync } = require('child_process');
const fs = require('fs');
const path = require('path');
const os = require('os');

const SCRIPT = `
import json, sys
from openpyxl import load_workbook

src, dst, payload = sys.argv[1], sys.argv[2], sys.argv[3]
rows = json.loads(payload)

wb = load_workbook(src)
ws = wb[wb.sheetnames[0]]

# Baris 2 pada template berisi contoh; ditimpa oleh data uji.
for r, row in enumerate(rows, start=2):
    for c, value in enumerate(row, start=1):
        ws.cell(row=r, column=c, value=value)

# Bersihkan sisa baris contoh bila data uji lebih pendek.
for r in range(2 + len(rows), ws.max_row + 1):
    for c in range(1, ws.max_column + 1):
        ws.cell(row=r, column=c, value=None)

wb.save(dst)
print("ok")
`;

exports.fill = async function fill(templatePath, outPath, rows) {
  const scriptPath = path.join(os.tmpdir(), `cuan-fill-${Date.now()}.py`);
  fs.writeFileSync(scriptPath, SCRIPT, 'utf8');
  try {
    execFileSync('python', [scriptPath, templatePath, outPath, JSON.stringify(rows)], {
      stdio: ['ignore', 'pipe', 'pipe'],
    });
  } finally {
    fs.unlinkSync(scriptPath);
  }
  return outPath;
};
