import { acceptHMRUpdate, defineStore } from 'pinia';
import { computed, reactive, toRefs } from 'vue';
import type { Observable } from 'rxjs';
import { of } from 'rxjs';
import { StoreNames, type ISetupResult } from '@interfaces';

/**
 * The available application themes.
 *
 * - `modern`  the current Reaparr look: hero headers, translucent surfaces, accent gradients.
 * - `classic` the original stock look, with the modern styling and hero markup switched off.
 */
export type ReaparrTheme = 'modern' | 'classic';

export const REAPARR_THEMES: ReaparrTheme[] = ['modern', 'classic'];

export const THEME_STORAGE_KEY = 'reaparr-theme';
export const DEFAULT_THEME: ReaparrTheme = 'modern';

/**
 * The attribute the theme is published on. Both theme stylesheets are scoped to it, so switching
 * this one value turns the entire modern skin on or off without reloading anything.
 */
export const THEME_ATTRIBUTE = 'data-reaparr-theme';

/** Reads the stored theme, tolerating unset, invalid, and inaccessible storage. */
export function readStoredTheme(): ReaparrTheme {
	if (typeof localStorage === 'undefined') {
		return DEFAULT_THEME;
	}

	try {
		const stored = localStorage.getItem(THEME_STORAGE_KEY);
		return isTheme(stored) ? stored : DEFAULT_THEME;
	} catch {
		// Private browsing and blocked site-data both throw on access.
		return DEFAULT_THEME;
	}
}

/** Publishes the theme to the document root, where the stylesheets are scoped to it. */
export function applyThemeAttribute(theme: ReaparrTheme): void {
	if (typeof document === 'undefined') {
		return;
	}

	document.documentElement.setAttribute(THEME_ATTRIBUTE, theme);
}

interface IThemeStoreState {
	theme: ReaparrTheme;
}

function isTheme(value: unknown): value is ReaparrTheme {
	return typeof value === 'string' && REAPARR_THEMES.includes(value as ReaparrTheme);
}

export const useThemeStore = defineStore(StoreNames.ThemeStore, () => {
	const state = reactive<IThemeStoreState>({ theme: DEFAULT_THEME });

	const actions = {
		setup(): Observable<ISetupResult> {
			actions.load();
			return of({ name: StoreNames.ThemeStore, isSuccess: true });
		},

		/** Reads the stored preference and applies it. Falls back to the default when unset or invalid. */
		load(): void {
			state.theme = readStoredTheme();
			applyThemeAttribute(state.theme);
		},

		setTheme(theme: ReaparrTheme): void {
			if (!isTheme(theme) || state.theme === theme) {
				return;
			}

			state.theme = theme;
			applyThemeAttribute(theme);

			try {
				localStorage.setItem(THEME_STORAGE_KEY, theme);
			} catch {
				// Preference is not persisted, but the current session still switches.
			}
		},
	};

	const getters = {
		/** True while the modern skin is active. Drives the theme-only markup in pages. */
		isModern: computed(() => state.theme === 'modern'),
		isClassic: computed(() => state.theme === 'classic'),
	};

	return {
		...toRefs(state),
		...actions,
		...getters,
	};
});

if (import.meta.hot) {
	import.meta.hot.accept(acceptHMRUpdate(useThemeStore, import.meta.hot));
}
