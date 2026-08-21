import { defineNuxtPlugin } from '#app';
import { applyThemeAttribute, readStoredTheme } from '@store';

/**
 * Applies the stored theme to <html> before anything renders.
 *
 * Numbered 0 so it runs ahead of the other client plugins. Without this the page would paint in
 * the default theme and then switch once the store initialised, which reads as a flash of the
 * wrong skin on every load. Deliberately does not touch Pinia - it only needs localStorage and a
 * DOM attribute, so it can run as early as possible.
 */
export default defineNuxtPlugin(() => {
	applyThemeAttribute(readStoredTheme());
});
