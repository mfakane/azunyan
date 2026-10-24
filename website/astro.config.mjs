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
			sidebar: [
				{
					label: 'Azunote',
					items: [
						{ label: 'Overview', link: '/' },
						{ label: 'Download', slug: 'download' },
					],
				},
				{
					label: 'Documentation',
					items: [
						{ label: 'Command line', slug: 'docs/command-line' },
						{ label: 'Settings', slug: 'docs/settings' },
						{ label: 'External tools', slug: 'docs/external-tools' },
						{ label: 'Substitution variables', slug: 'docs/substitution-variables' },
					],
				},
			],
		}),
	],
});
