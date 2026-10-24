// Copies the user documentation and artwork maintained in the repository into
// the site, so docs/user and assets stay the only copies to edit.
import { copyFile, mkdir, readdir, readFile, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const site = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const repo = path.resolve(site, '..');
const docsSource = path.join(repo, 'docs', 'user');
const docsTarget = path.join(site, 'src', 'content', 'docs', 'docs');
const repoBlob = 'https://github.com/mfakane/azunyan/blob/master/';
// Matches `base` in astro.config.mjs.
const docsBase = '/azunyan/docs/';

// A link to a sibling page becomes that page's route; any other relative
// link points at the file on GitHub.
function rewriteLink(target) {
	if (/^([a-z]+:|#|\/)/i.test(target)) return target;
	const [file, hash = ''] = target.split('#');
	const suffix = hash ? `#${hash}` : '';
	if (!file.includes('/') && file.endsWith('.md')) return `${docsBase}${file.slice(0, -3)}/${suffix}`;
	const resolved = path.relative(repo, path.resolve(docsSource, file)).replaceAll('\\', '/');
	return repoBlob + resolved + suffix;
}

function convert(markdown, name) {
	const match = markdown.match(/^# (.+)\r?\n/);
	if (!match) throw new Error(`docs/user/${name} must start with a level-1 heading.`);
	const body = markdown
		.slice(match[0].length)
		.replace(/(\]\()([^)\s]+)(\))/g, (_, open, target, close) => open + rewriteLink(target) + close);
	return `---\ntitle: ${JSON.stringify(match[1])}\n---\n${body}`;
}

await rm(docsTarget, { recursive: true, force: true });
await mkdir(docsTarget, { recursive: true });
for (const name of await readdir(docsSource)) {
	if (!name.endsWith('.md')) continue;
	const markdown = await readFile(path.join(docsSource, name), 'utf8');
	await writeFile(path.join(docsTarget, name), convert(markdown, name));
}

const assets = path.join(site, 'src', 'assets', 'repo');
const publicDir = path.join(site, 'public');
await mkdir(assets, { recursive: true });
await mkdir(publicDir, { recursive: true });
await copyFile(path.join(repo, 'assets', 'icons', '01-paw-notebook.svg'), path.join(assets, 'icon.svg'));
await copyFile(path.join(repo, 'assets', 'icons', '01-paw-notebook-small.svg'), path.join(assets, 'logo.svg'));
await copyFile(path.join(repo, 'assets', 'screenshots', 'completion.png'), path.join(assets, 'completion.png'));
await copyFile(path.join(repo, 'assets', 'icons', '01-paw-notebook-16.svg'), path.join(publicDir, 'favicon.svg'));
