import { defineConfig, type Plugin } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'
import icons from 'unplugin-icons/vite'
import * as vueExports from 'vue'

/**
 * Plugins share the web UI's Vue. The host puts its API object, with `vue`, on `globalThis.__odysseum` before it imports
 * the plugin, so every `import ... from 'vue'` (also in compiled templates and icons) resolves to that copy.
 */
function hostVue(): Plugin {
  const id = '\0odysseum-host-vue'
  const names = Object.keys(vueExports).filter(name => name !== 'default' && /^[A-Za-z_$][\w$]*$/.test(name))
  return {
    name: 'odysseum-host-vue',
    enforce: 'pre',
    resolveId: source => source === 'vue' ? id : null,
    load: loaded => loaded === id
      ? `const vue = globalThis.__odysseum.vue;\nexport const { ${names.join(', ')} } = vue;\nexport default vue;\n`
      : null,
  }
}

export default defineConfig({
  plugins: [hostVue(), vue(), tailwindcss(), icons({ compiler: 'vue3' })],
  define: { 'process.env.NODE_ENV': JSON.stringify('production') },
  build: {
    outDir: '../wwwroot',
    emptyOutDir: true,
    lib: { entry: 'src/index.ts', formats: ['es'], fileName: () => 'index.js', cssFileName: 'index' },
  },
})
