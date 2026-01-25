const esbuild = require('esbuild');
const path = require('path');

async function bundle() {
  const common = {
    entryPoints: [path.join(__dirname, '../src/index.ts')],
    bundle: true,
    sourcemap: true,
    target: ['es2020'],
  };

  // ESM bundle
  await esbuild.build({
    ...common,
    outfile: 'dist/smartinspect.esm.js',
    format: 'esm',
  });

  // UMD/IIFE bundle for browsers
  await esbuild.build({
    ...common,
    outfile: 'dist/smartinspect.js',
    format: 'iife',
    globalName: 'SmartInspectJS',
  });

  // Minified browser bundle
  await esbuild.build({
    ...common,
    outfile: 'dist/smartinspect.min.js',
    format: 'iife',
    globalName: 'SmartInspectJS',
    minify: true,
  });

  console.log('Bundles created successfully!');
}

bundle().catch((err) => {
  console.error(err);
  process.exit(1);
});
