const fs = require('fs');
const path = require('path');
const { createCanvas, DOMMatrix, ImageData, Path2D } = require('@napi-rs/canvas');

global.DOMMatrix = DOMMatrix;
global.ImageData = ImageData;
global.Path2D = Path2D;

const [pdfPath, outputDirectory] = process.argv.slice(2);
if (!pdfPath || !outputDirectory) {
  throw new Error('Usage: node render_pdf.cjs input.pdf output-directory');
}

(async () => {
  const pdfjsPath = 'file:///C:/Users/Admin/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/pdfjs-dist/legacy/build/pdf.mjs';
  const pdfjs = await import(pdfjsPath);
  const data = new Uint8Array(fs.readFileSync(pdfPath));
  const pdf = await pdfjs.getDocument({
    data,
    disableWorker: true,
    standardFontDataUrl: 'C:/Users/Admin/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/pdfjs-dist/standard_fonts/',
  }).promise;
  fs.mkdirSync(outputDirectory, { recursive: true });
  console.log(`pages=${pdf.numPages}`);
  for (let pageNumber = 1; pageNumber <= pdf.numPages; pageNumber++) {
    const page = await pdf.getPage(pageNumber);
    const viewport = page.getViewport({ scale: 1.7 });
    const canvas = createCanvas(Math.ceil(viewport.width), Math.ceil(viewport.height));
    const context = canvas.getContext('2d');
    context.fillStyle = '#ffffff';
    context.fillRect(0, 0, canvas.width, canvas.height);
    await page.render({ canvasContext: context, viewport }).promise;
    const output = path.join(outputDirectory, `page-${String(pageNumber).padStart(2, '0')}.png`);
    fs.writeFileSync(output, canvas.toBuffer('image/png'));
    console.log(output);
  }
})();
