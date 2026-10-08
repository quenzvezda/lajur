// Development-only exports; the applications have no image-generation dependency.
// Requires Node.js and sharp. An optional argument supplies the sharp module path.
const fs = require('node:fs');
const path = require('node:path');
const sharp = require(process.argv[2] || 'sharp');

const root = path.resolve(__dirname, '../../..');
const brand = __dirname;
const web = path.join(root, 'services/api/src/Lajur.Api/wwwroot/brand');
const res = path.join(root, 'apps/android/app/src/main/res');
const mark = fs.readFileSync(path.join(brand, 'lajur-mark.svg'), 'utf8');
const paths = [...mark.matchAll(/<path fill="([^"]+)" d="([^"]+)"\/>/g)];
if (paths.length !== 3) throw new Error('Expected the three Lajur mark paths.');
const shapes = paths.map(match => match[0]).join('\n');
const icon = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="18 18 72 72"><path fill="#0F6E63" d="M18 18H90V90H18Z"/>${shapes}</svg>\n`;
const logo = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 360 100"><rect width="100" height="100" rx="22" fill="#0F6E63"/><g transform="translate(25 25) scale(.926) translate(-27 -27)">${shapes}</g><text x="120" y="71" fill="#0F6E63" font-family="system-ui,Segoe UI,sans-serif" font-size="64" font-weight="700" letter-spacing="-2">Lajur</text></svg>\n`;

function write(file, text) {
  fs.mkdirSync(path.dirname(file), {recursive: true});
  fs.writeFileSync(file, text);
}
async function png(svg, size, destination) {
  fs.mkdirSync(path.dirname(destination), {recursive: true});
  await sharp(Buffer.from(svg)).resize(size, size).png().toFile(destination);
}
function vector(viewport, body) {
  return `<?xml version="1.0" encoding="utf-8"?>\n<vector xmlns:android="http://schemas.android.com/apk/res/android" android:width="${viewport}dp" android:height="${viewport}dp" android:viewportWidth="${viewport}" android:viewportHeight="${viewport}">\n${body}\n</vector>\n`;
}

async function main() {
  write(path.join(brand, 'lajur-icon.svg'), icon);
  write(path.join(brand, 'lajur-logo.svg'), logo);
  const darkLogo = logo.replace('y="71" fill="#0F6E63"', 'y="71" fill="#E8EAED"');
  write(path.join(brand, 'lajur-logo-dark.svg'), darkLogo);
  write(path.join(web, 'lajur-icon.svg'), icon);
  write(path.join(web, 'lajur-logo.svg'), logo);
  await png(mark, 1024, path.join(brand, 'lajur-mark.png'));
  await png(icon, 1024, path.join(brand, 'lajur-icon-1024.png'));
  await png(icon, 512, path.join(brand, 'lajur-icon-512.png'));
  await sharp(Buffer.from(logo)).resize({width: 1440}).png().toFile(path.join(brand, 'lajur-logo.png'));
  await sharp(Buffer.from(darkLogo)).resize({width: 1440}).png().toFile(path.join(brand, 'lajur-logo-dark.png'));

  const androidPaths = paths.map(match => `    <path android:fillColor="${match[1]}" android:pathData="${match[2]}" />`).join('\n');
  write(path.join(res, 'drawable/ic_launcher_foreground.xml'), vector(108, androidPaths));
  write(path.join(res, 'drawable/ic_launcher_background.xml'), vector(108, '    <path android:fillColor="#0F6E63" android:pathData="M0,0H108V108H0Z" />'));
  write(path.join(res, 'drawable/ic_lajur_logo.xml'), vector(72,
    `    <path android:fillColor="#0F6E63" android:pathData="M0,0H72V72H0Z" />\n    <group android:translateX="-18" android:translateY="-18">\n${androidPaths}\n    </group>`));
  for (const [density, size] of [['mdpi',48], ['hdpi',72], ['xhdpi',96], ['xxhdpi',144], ['xxxhdpi',192]]) {
    const folder = path.join(res, `mipmap-${density}`);
    const pixels = await sharp(Buffer.from(icon)).resize(size, size).png().toBuffer();
    await sharp(pixels).webp({lossless:true}).toFile(path.join(folder, 'ic_launcher.webp'));
    const mask = Buffer.from(`<svg width="${size}" height="${size}"><circle cx="${size/2}" cy="${size/2}" r="${size/2}" fill="white"/></svg>`);
    await sharp(pixels).composite([{input:mask,blend:'dest-in'}]).webp({lossless:true}).toFile(path.join(folder, 'ic_launcher_round.webp'));
  }
  for (const [size, name] of [[16,'favicon-16.png'],[32,'favicon-32.png'],[180,'apple-touch-icon.png'],[192,'icon-192.png'],[512,'icon-512.png']]) {
    await png(icon, size, path.join(web, name));
  }
  // ICO entries contain PNG payloads supported by current desktop browsers.
  const frames = await Promise.all([16,32,48].map(size => sharp(Buffer.from(icon)).resize(size,size).png().toBuffer()));
  const header = Buffer.alloc(6 + frames.length*16);
  header.writeUInt16LE(1,2); header.writeUInt16LE(frames.length,4);
  let offset = header.length;
  frames.forEach((frame, i) => {
    const p = 6+i*16, size = [16,32,48][i];
    header[p]=size; header[p+1]=size;
    header.writeUInt16LE(1,p+4); header.writeUInt16LE(32,p+6);
    header.writeUInt32LE(frame.length,p+8); header.writeUInt32LE(offset,p+12);
    offset += frame.length;
  });
  fs.writeFileSync(path.join(root, 'services/api/src/Lajur.Api/wwwroot/favicon.ico'),Buffer.concat([header,...frames]));
  console.log('Exported SVG/PNG, five Android densities, adaptive vectors, and web icons.');
}
main().catch(error => {console.error(error); process.exitCode=1;});
