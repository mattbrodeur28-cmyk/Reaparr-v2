<template>
	<slot v-if="themeStore.isModern" />
</template>

<script setup lang="ts">
import { useThemeStore } from '@store';

/**
 * Renders its content only under the Modern theme.
 *
 * The Modern skin is not purely cosmetic: several pages add hero headers with their own titles and
 * descriptions. Scoping the stylesheets is enough to remove the *styling* in Classic, but that
 * markup would still render, as unstyled headings stacked above the real content. Wrapping it here
 * removes it entirely, so Classic gets the stock page back rather than a stripped-down Modern one.
 *
 * Auto-imported, so pages need no script changes to use it.
 */
const themeStore = useThemeStore();
</script>
