<template>
	<QPage class="v7-page v7-settings-page">
		<ThemeOnly>
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
						Connect Sonarr, Radarr, Lidarr and TMDB, then configure post-download Plex and library reconciliation.
					</p>
				</div>
			</section>
		</ThemeOnly>

		<section class="v7-settings-stack">
			<q-expansion-item
				v-model="publicApiExpanded"
				class="v77-integration-expansion"
				header-class="v77-integration-expansion__header"
				icon="mdi-key-variant"
				:label="$t('components.public-api-integration.title')"
				caption="API key and credentials for SoulSync, Prowlarr and Torznab clients">
				<div class="v77-integration-expansion__content">
					<PublicApiIntegration />
				</div>
			</q-expansion-item>

			<q-expansion-item
				v-model="sonarrExpanded"
				class="v77-integration-expansion"
				header-class="v77-integration-expansion__header"
				icon="mdi-television-classic"
				:label="$t('components.sonarr-integration.title')"
				caption="TV wanted/missing integration">
				<div class="v77-integration-expansion__content">
					<SonarrIntegration />
				</div>
			</q-expansion-item>

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
				v-model="lidarrExpanded"
				class="v77-integration-expansion"
				header-class="v77-integration-expansion__header"
				icon="mdi-music-note"
				:label="$t('components.lidarr-integration.title')"
				caption="Music wanted/missing integration, monitored by Lidarr">
				<div class="v77-integration-expansion__content">
					<LidarrIntegration />
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

			<q-expansion-item
				v-model="libraryReconciliationExpanded"
				class="v77-integration-expansion"
				header-class="v77-integration-expansion__header"
				icon="mdi-sync"
				label="Library Reconciliation"
				caption="Post-download Plex library reconciliation">
				<div class="v77-integration-expansion__content">
					<LibraryReconciliationIntegration />
				</div>
			</q-expansion-item>
		</section>
	</QPage>
</template>

<script setup lang="ts">
import { useLocalStorage } from '@vueuse/core';

const radarrExpanded = useLocalStorage<boolean>(
	'reaparr-settings-radarr-expanded',
	false,
);

const sonarrExpanded = useLocalStorage<boolean>(
	'reaparr-settings-sonarr-expanded',
	false,
);

const libraryReconciliationExpanded = useLocalStorage<boolean>(
	'reaparr-settings-library-reconciliation-expanded',
	false,
);

const lidarrExpanded = useLocalStorage<boolean>(
	'reaparr-settings-lidarr-expanded',
	false,
);

const tmdbExpanded = useLocalStorage<boolean>(
	'reaparr-settings-tmdb-expanded',
	false,
);

const publicApiExpanded = useLocalStorage<boolean>(
	'reaparr-settings-public-api-expanded',
	false,
);
</script>
