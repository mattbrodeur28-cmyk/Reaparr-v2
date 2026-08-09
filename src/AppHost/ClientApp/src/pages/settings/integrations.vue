<template>
	<QPage class="v7-page v7-settings-page">
		<section class="v7-page-hero v7-page-hero--compact v7-page-hero--integrations">
			<div>
				<div class="v7-page-kicker">
					<q-icon name="mdi-connection" />
					Connected ecosystem
				</div>
				<h1 class="v7-page-title">
					Integrations
				</h1>
				<p class="v7-page-subtitle">
					Connect Sonarr, Radarr and TMDB, then configure post-download Plex and library reconciliation.
				</p>
			</div>
		</section>

		<section class="v7-settings-stack">
			<SonarrIntegration />

			<q-expansion-item
				v-model="radarrExpanded"
				class="v77-integration-expansion"
				header-class="v77-integration-expansion__header"
				icon="mdi-radar"
				:label="$t('components.radarr-integration.title')"
				caption="Movie wanted/missing integration">
				<div class="v77-integration-expansion__content">
					<RadarrIntegration />
				</div>
			</q-expansion-item>

			<q-expansion-item
				v-model="tmdbExpanded"
				class="v77-integration-expansion"
				header-class="v77-integration-expansion__header"
				icon="mdi-movie-search-outline"
				label="TMDB API"
				caption="Identity enrichment and external-ID matching">
				<div class="v77-integration-expansion__content">
					<TmdbIntegration />
				</div>
			</q-expansion-item>

			<LibraryReconciliationIntegration />
			<MediaAutomationIntegration />
		</section>
	</QPage>
</template>

<script setup lang="ts">
import { useLocalStorage } from '@vueuse/core';

const radarrExpanded = useLocalStorage<boolean>(
	'reaparr-settings-radarr-expanded',
	false,
);

const tmdbExpanded = useLocalStorage<boolean>(
	'reaparr-settings-tmdb-expanded',
	false,
);
</script>

<style lang="scss">
.v77-integration-expansion {
  overflow: hidden;
  border: 1px solid var(--v5-border);
  border-radius: 20px;
  background: var(--v5-surface);
  box-shadow: var(--v5-shadow-sm);
}

.v77-integration-expansion__header {
  min-height: 62px;
  padding: 10px 16px;
}

.v77-integration-expansion__content {
  padding: 0 10px 10px;
}

.v77-integration-expansion__content > * {
  border: 0 !important;
  box-shadow: none !important;
}
</style>
