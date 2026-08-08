<template>
	<q-page class="discover-page q-pa-lg">
		<div class="discover-header">
			<div>
				<div class="text-h3 text-weight-bold">
					Discover
				</div>
				<div class="text-subtitle1 text-grey-5 q-mt-xs">
					One title per card. Reaparr automatically chooses the best online source when you download.
				</div>
				<div
					v-if="discoverStore.lastUpdatedAt"
					class="text-caption text-grey-6 q-mt-xs">
					{{ cacheLabel }}
				</div>
			</div>

			<q-btn
				color="primary"
				icon="mdi-refresh"
				label="Refresh"
				:loading="discoverStore.refreshing"
				data-cy="discover-refresh"
				@click="refresh" />
		</div>

		<div class="discover-stats q-mt-lg">
			<q-card
				flat
				bordered
				class="discover-stat-card">
				<q-card-section>
					<div class="text-caption text-grey-5">
						Showing
					</div>
					<div class="text-h4 text-weight-bold">
						{{ filteredItems.length }}
					</div>
				</q-card-section>
			</q-card>

			<q-card
				flat
				bordered
				class="discover-stat-card">
				<q-card-section>
					<div class="text-caption text-grey-5">
						Missing / Incomplete
					</div>
					<div class="text-h4 text-weight-bold">
						{{ missingCount }}
					</div>
				</q-card-section>
			</q-card>

			<q-card
				flat
				bordered
				class="discover-stat-card">
				<q-card-section>
					<div class="text-caption text-grey-5">
						Upgrades
					</div>
					<div class="text-h4 text-weight-bold">
						{{ upgradeCount }}
					</div>
				</q-card-section>
			</q-card>

			<q-card
				flat
				bordered
				class="discover-stat-card">
				<q-card-section>
					<div class="text-caption text-grey-5">
						Extra duplicate sources merged
					</div>
					<div class="text-h4 text-weight-bold">
						{{ mergedSourceCount }}
					</div>
				</q-card-section>
			</q-card>
		</div>

		<q-card
			flat
			bordered
			class="discover-toolbar q-mt-lg">
			<q-card-section class="discover-toolbar-content">
				<q-input
					v-model="search"
					outlined
					dense
					clearable
					debounce="150"
					placeholder="Search Discover"
					class="discover-search"
					data-cy="discover-search">
					<template #prepend>
						<q-icon name="mdi-magnify" />
					</template>
				</q-input>

				<q-btn-toggle
					v-model="mediaTypeFilter"
					unelevated
					no-caps
					toggle-color="primary"
					:options="mediaTypeOptions"
					data-cy="discover-media-type-filter" />

				<q-btn-toggle
					v-model="reasonFilter"
					unelevated
					no-caps
					toggle-color="primary"
					:options="reasonOptions"
					data-cy="discover-reason-filter" />

				<q-separator
					vertical
					class="discover-toolbar-separator" />

				<div class="discover-wanted-toggle">
					<q-toggle
						v-model="wantedOnly"
						color="primary"
						:disable="!discoverStore.arrConfigured"
						label="Sonarr / Radarr missing only" />
					<div class="text-caption text-grey-6">
						Default: limit missing titles to *arr; upgrade opportunities still appear
					</div>
				</div>
			</q-card-section>
		</q-card>

		<q-banner
			v-if="!discoverStore.arrConfigured"
			class="bg-info text-white q-mt-md rounded-borders">
			Configure Radarr and/or Sonarr in Reaparr to enable the wanted-only Discover filter.
		</q-banner>

		<q-banner
			v-else-if="!discoverStore.arrDataAvailable"
			class="bg-warning text-dark q-mt-md rounded-borders">
			The Sonarr/Radarr wanted list is unavailable right now, so Discover is temporarily showing the full Plex missing/upgrade feed.
		</q-banner>

		<q-banner
			v-if="discoverStore.arrWarnings.length"
			class="bg-warning text-dark q-mt-md rounded-borders">
			<div
				v-for="warning in discoverStore.arrWarnings"
				:key="warning">
				{{ warning }}
			</div>
		</q-banner>

		<q-banner
			v-if="discoverStore.errorMessage"
			class="bg-warning text-dark q-mt-md rounded-borders">
			{{ discoverStore.errorMessage }}
		</q-banner>

		<div
			v-if="discoverStore.loading && !discoverStore.items.length"
			class="discover-loading">
			<QSpinner
				size="48px"
				color="primary" />
			<div class="text-subtitle1 q-mt-md">
				Building your Discover list…
			</div>
			<div class="text-caption text-grey-5">
				{{ discoverStore.completedQueries }} / {{ discoverStore.totalQueries }} library checks complete
			</div>
		</div>

		<template v-else>
			<div
				v-if="discoverStore.refreshing && discoverStore.items.length"
				class="discover-refreshing text-caption text-grey-5 q-mt-md">
				<QSpinner
					size="18px"
					class="q-mr-sm" />
				Refreshing in the background — cached results stay visible.
			</div>

			<div
				v-if="filteredItems.length"
				class="discover-grid q-mt-lg"
				data-cy="discover-grid">
				<div
					v-for="item in filteredItems"
					:key="item.key"
					class="discover-item">
					<div class="discover-reason-row">
						<q-chip
							dense
							:color="reasonColor(item.comparisonState)"
							text-color="white"
							:icon="reasonIcon(item.comparisonState)">
							{{ reasonLabel(item.comparisonState) }}
						</q-chip>

						<q-chip
							v-if="item.sources.length > 1"
							dense
							outline
							color="secondary"
							icon="mdi-server-network">
							{{ item.sources.length }} sources
						</q-chip>
					</div>

					<MediaPoster
						:media-item="item.media"
						@download="handleDownload($event, item)"
						@open-media-details="openMediaDetails" />

					<div class="discover-source text-caption text-grey-5">
						<div class="discover-source-line">
							<q-icon
								:name="discoverStore.isSourceOnline(item.media.plexServerId) ? 'mdi-server-network' : 'mdi-server-network-off'"
								:color="discoverStore.isSourceOnline(item.media.plexServerId) ? 'positive' : 'negative'"
								size="16px" />
							<span>
								Best: {{ serverStore.getServerName(item.media.plexServerId) }}
							</span>
						</div>
						<div>
							{{ qualityLabel(item.media) }}
							<span v-if="item.wantedBy.length">
								• Wanted by {{ item.wantedBy.join(' + ') }}
							</span>
						</div>
					</div>
				</div>
			</div>

			<div
				v-else
				class="discover-empty q-mt-xl">
				<q-icon
					name="mdi-check-decagram-outline"
					size="64px"
					color="positive" />
				<div class="text-h5 q-mt-md">
					Nothing to discover with these filters
				</div>
				<div class="text-body2 text-grey-5 q-mt-sm">
					Try turning off the Sonarr/Radarr missing-only flag, or refresh after your Plex and *arr libraries update.
				</div>
			</div>
		</template>

		<MediaComparisonDetailsDialog />
		<DownloadConfirmation @download="downloadStore.downloadMedia($event)" />
	</q-page>
</template>

<script setup lang="ts">
import { get, set, useLocalStorage } from '@vueuse/core';
import { useQuasar } from 'quasar';
import { useSubscription } from '@vueuse/rxjs';
import {
	PlexMediaComparisonState,
	PlexMediaType,
	VideoQuality,
	type DownloadMediaDTO,
	type PlexMediaSlimDTO,
} from '@dto';
import {
	useDialogStore,
	useDiscoverStore,
	useDownloadStore,
	useServerStore,
	useSettingsStore,
} from '@store';
import type { IDiscoverItem } from '@/store/discoverStore';

const discoverStore = useDiscoverStore();
const downloadStore = useDownloadStore();
const dialogStore = useDialogStore();
const settingsStore = useSettingsStore();
const serverStore = useServerStore();
const router = useRouter();
const $q = useQuasar();

const search = ref('');
const mediaTypeFilter = ref<'all' | 'movies' | 'tv'>('all');
const reasonFilter = ref<'all' | 'missing' | 'upgrades'>('all');
const wantedOnly = useLocalStorage('reaparr-discover-wanted-only', true);

const mediaTypeOptions = [
	{ label: 'All', value: 'all' },
	{ label: 'Movies', value: 'movies' },
	{ label: 'TV Shows', value: 'tv' },
];

const reasonOptions = [
	{ label: 'All', value: 'all' },
	{ label: 'Missing', value: 'missing' },
	{ label: 'Upgrades', value: 'upgrades' },
];

const missingStates: PlexMediaComparisonState[] = [
	PlexMediaComparisonState.Missing,
	PlexMediaComparisonState.Partial,
	PlexMediaComparisonState.PartialAndHigherQuality,
];

const upgradeStates: PlexMediaComparisonState[] = [
	PlexMediaComparisonState.HigherQuality,
	PlexMediaComparisonState.PartialAndHigherQuality,
];

const applyWantedOnly = computed(() =>
	get(wantedOnly)
	&& discoverStore.arrConfigured
	&& discoverStore.arrDataAvailable,
);

const filteredItems = computed(() => {
	const query = get(search).trim().toLocaleLowerCase();
	const typeFilter = get(mediaTypeFilter);
	const reason = get(reasonFilter);

	return discoverStore.items.filter((item) => {
		if (get(applyWantedOnly) && !upgradeStates.includes(item.comparisonState) && !item.wantedByArr) {
			return false;
		}

		if (query && !item.media.title.toLocaleLowerCase().includes(query)) {
			return false;
		}

		if (typeFilter === 'movies' && item.media.type !== PlexMediaType.Movie) {
			return false;
		}
		if (typeFilter === 'tv' && item.media.type !== PlexMediaType.TvShow) {
			return false;
		}
		if (reason === 'missing' && !missingStates.includes(item.comparisonState)) {
			return false;
		}
		if (reason === 'upgrades' && !upgradeStates.includes(item.comparisonState)) {
			return false;
		}

		return true;
	});
});

const missingCount = computed(() =>
	get(filteredItems).filter((item) => missingStates.includes(item.comparisonState)).length,
);

const upgradeCount = computed(() =>
	get(filteredItems).filter((item) => upgradeStates.includes(item.comparisonState)).length,
);

const mergedSourceCount = computed(() =>
	discoverStore.items.reduce((count, item) => count + Math.max(0, item.sources.length - 1), 0),
);

const cacheLabel = computed(() => {
	if (!discoverStore.lastUpdatedAt) {
		return '';
	}

	const ageMinutes = Math.max(0, Math.round((Date.now() - discoverStore.lastUpdatedAt) / 60000));
	const source = discoverStore.loadedFromCache ? 'Cached feed' : 'Live feed';
	if (ageMinutes < 1) {
		return `${source} • updated just now`;
	}

	return `${source} • updated ${ageMinutes}m ago`;
});

function refresh() {
	useSubscription(discoverStore.refresh().subscribe());
}

function reasonLabel(state: PlexMediaComparisonState): string {
	switch (state) {
		case PlexMediaComparisonState.Missing:
			return 'Missing';
		case PlexMediaComparisonState.HigherQuality:
			return 'Upgrade available';
		case PlexMediaComparisonState.Partial:
			return 'Incomplete';
		case PlexMediaComparisonState.PartialAndHigherQuality:
			return 'Incomplete + upgrade';
		default:
			return 'Discover';
	}
}

function reasonColor(state: PlexMediaComparisonState): string {
	if (state === PlexMediaComparisonState.HigherQuality || state === PlexMediaComparisonState.PartialAndHigherQuality) {
		return 'positive';
	}
	return 'primary';
}

function reasonIcon(state: PlexMediaComparisonState): string {
	if (state === PlexMediaComparisonState.HigherQuality || state === PlexMediaComparisonState.PartialAndHigherQuality) {
		return 'mdi-arrow-up-bold-circle-outline';
	}
	return 'mdi-plus-circle-outline';
}

function qualityLabel(media: PlexMediaSlimDTO): string {
	switch (discoverStore.getBestQuality(media)) {
		case VideoQuality.UHD_8K:
			return '8K';
		case VideoQuality.UHD_4K:
			return '4K';
		case VideoQuality.QHD:
			return '1440p';
		case VideoQuality.FullHD:
			return '1080p';
		case VideoQuality.HD:
			return '720p';
		case VideoQuality.DVD:
			return 'DVD';
		case VideoQuality.SD:
			return 'SD';
		default:
			return 'Quality unknown';
	}
}

function handleDownload(command: DownloadMediaDTO[], item: IDiscoverItem) {
	if (!command.length || !command.some((download) => download.mediaIds.length > 0)) {
		return;
	}

	const requestedQualities = [
		...new Set(command.flatMap((download) => download.qualities.map((quality) => quality.quality))),
	];

	const bestSource = discoverStore.selectBestSource(item, requestedQualities, true);
	if (!bestSource) {
		$q.notify({
			type: 'warning',
			message: requestedQualities.length
				? 'No online source currently has the selected quality.'
				: 'No online, available source is currently available for this title.',
		});
		return;
	}

	const smartCommand: DownloadMediaDTO[] = [{
		type: bestSource.media.type,
		mediaIds: [bestSource.media.id],
		plexLibraryId: bestSource.media.plexLibraryId,
		plexServerId: bestSource.media.plexServerId,
		qualities: discoverStore.getMatchingQualities(bestSource.media, requestedQualities),
		keepCompletedInDownloadFolder: settingsStore.downloadManagerSettings.keepCompletedInDownloadFolder,
	}];

	const mediaType = smartCommand[0]?.type ?? PlexMediaType.Unknown;
	if (settingsStore.isConfirmationEnabled(mediaType)) {
		dialogStore.openMediaConfirmationDownloadDialog(smartCommand);
		return;
	}

	downloadStore.downloadMedia({
		customDestinationFolderPath: '',
		destinationFolderPathId: null,
		downloadMedias: smartCommand,
	});
}

function openMediaDetails(mediaItem: PlexMediaSlimDTO) {
	if (mediaItem.type === PlexMediaType.Movie) {
		router.push({
			name: 'movies-libraryId-details-movieId',
			params: {
				libraryId: mediaItem.plexLibraryId.toString(),
				movieId: mediaItem.id.toString(),
			},
		});
		return;
	}

	router.push({
		name: 'tvshows-libraryId-details-tvShowId',
		params: {
			libraryId: mediaItem.plexLibraryId.toString(),
			tvShowId: mediaItem.id.toString(),
		},
	});
}

onMounted(() => {
	set(search, '');
	useSubscription(discoverStore.initialize().subscribe());
});
</script>

<style lang="scss">
@use '@/assets/scss/variables.scss' as *;

.discover-page {
  min-height: 100%;
}

.discover-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 24px;
}

.discover-stats {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 16px;
}

.discover-stat-card,
.discover-toolbar {
  background: rgba(255, 255, 255, 0.04);
}

.discover-toolbar-content {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 16px;
}

.discover-search {
  width: min(420px, 100%);
  flex: 1 1 300px;
}

.discover-toolbar-separator {
  min-height: 42px;
}

.discover-wanted-toggle {
  min-width: 260px;
}

.discover-loading,
.discover-empty {
  min-height: 360px;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  text-align: center;
}

.discover-empty {
  max-width: 620px;
  margin-left: auto;
  margin-right: auto;
}

.discover-refreshing {
  display: flex;
  align-items: center;
}

.discover-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(232px, 232px));
  justify-content: center;
  gap: 20px 12px;
}

.discover-item {
  width: 232px;
  min-width: 232px;
}

.discover-reason-row {
  min-height: 32px;
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  padding: 0 12px;
}

.discover-source {
  padding: 4px 16px 0;
  overflow: hidden;
}

.discover-source-line {
  display: flex;
  align-items: center;
  gap: 5px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

@media (max-width: 900px) {
  .discover-stats {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 700px) {
  .discover-page {
    padding: 16px;
  }

  .discover-header {
    align-items: flex-start;
  }

  .discover-stats {
    grid-template-columns: 1fr;
  }

  .discover-toolbar-content {
    align-items: stretch;
  }

  .discover-toolbar-separator {
    display: none;
  }
}
</style>
