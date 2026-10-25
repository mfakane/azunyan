// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

// https://astro.build/config
export default defineConfig({
	site: 'https://mfakane.github.io',
	base: '/azunyan',
	integrations: [
		starlight({
			title: 'Azunote',
			description: 'A small document-per-window text editor for Windows.',
			logo: { src: './src/assets/repo/logo.svg' },
			customCss: ['./src/styles/theme.css'],
			social: [{ icon: 'github', label: 'GitHub', href: 'https://github.com/mfakane/azunyan' }],
			// Starlight writes the other Open Graph and Twitter Card tags itself.
			head: [
				{ tag: 'meta', attrs: { property: 'og:image', content: 'https://mfakane.github.io/azunyan/og-image.png' } },
				{ tag: 'meta', attrs: { property: 'og:image:width', content: '1200' } },
				{ tag: 'meta', attrs: { property: 'og:image:height', content: '630' } },
				{ tag: 'meta', attrs: { property: 'og:image:alt', content: 'Azunote: a small document-per-window text editor for Windows.' } },
			],
			sidebar: [
				{
					label: 'Azunote',
					items: [
						{ label: 'Overview', link: '/' },
						{ label: 'Download', slug: 'download' },
					],
				},
				{
					label: 'Using Azunote',
					items: ['docs/command-line', 'docs/documents'],
				},
				{
					label: 'Configuration',
					items: ['docs/settings', 'docs/language-modes', 'docs/language-servers', 'docs/substitution-variables'],
				},
				{
					label: 'External tools',
					items: [
						{ label: 'Overview', slug: 'docs/external-tools' },
						'docs/tool-definitions',
						'docs/powershell-tools',
						'docs/prompt-tools',
						'docs/problems',
						'docs/proposed-changes',
						'docs/streaming-output',
					],
				},
			],
		}),
	],
});
