// @ts-check
// docs.playdex.dev: how to run Playdex on your own server. A static Starlight site on Cloudflare Pages,
// deployed by .github/workflows/docs.yml.
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

export default defineConfig({
  site: 'https://docs.playdex.dev',
  integrations: [
    starlight({
      title: 'Playdex docs',
      description: 'Run Playdex on your own server.',
      favicon: '/favicon.ico',
      social: [{ icon: 'github', label: 'GitHub', href: 'https://github.com/ivankaizer/playdex' }],
      editLink: { baseUrl: 'https://github.com/ivankaizer/playdex/edit/main/src/Docs/' },
      customCss: ['./src/styles/custom.css'],
      sidebar: [
        { label: 'Start here', items: ['index'] },
        { label: 'Self-hosting', items: [{ autogenerate: { directory: 'self-host' } }] },
        { label: 'Reference', items: [{ autogenerate: { directory: 'reference' } }] },
      ],
    }),
  ],
});
