<template>
	<q-page class="discover-page q-pa-lg">
		<div class="discover-header">
			<div>
				<div class="text-h3 text-weight-bold">
					Discover
				</div>
				<div class="text-subtitle1 text-grey-5 q-mt-xs">
					Only media you are missing, partially missing, or can upgrade.
				</div>
			</div>
			<q-btn
				color="primary"
				icon="mdi-refresh"
				label="Refresh"
				:loading="discoverStore.loading"
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
						Discoverable
					</div>
					<div class="text-h4 text-weight-bold">
						{{ discoverStore.items.length }}
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
			</q-card-section>
		</q-card>

		<q-banner
			v-if="discoverStore.errorMessage"
			class="bg-warning text-dark q-mt-md rounded-borders">
			{{ discoverStore.errorMessage }}
		</q-banner>

		<div
			v-if="discoverStore.loading"
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
				v-if="filteredItems.length"
				class="discover-grid q-mt-lg"
				data-cy="discover-grid">
				<div
					v-for="item in filteredItems"
					:key="`${item.media.plexServerId}-${item.media.plexLibraryId}-${item.media.id}`"
					class="discover-item">
					<div class="discover-reason-row">
						<q-chip
							dense
							:color="reasonColor(item.comparisonState)"
							text-color="white"
							:icon="reasonIcon(item.comparisonState)">
							{{ reasonLabel(item.comparisonState) }}
						</q-chip>
					</div>

					<MediaPoster
						:media-item="item.media"
						@download="handleDownload"
						@open-media-details="openMediaDetails" />

					<div class="discover-source text-caption text-grey-5">
						{{ serverStore.getServerName(item.media.plexServerId) }}
						<span v-if="libraryStore.getLibraryName(item.media.plexLibraryId)">
							• {{ libraryStore.getLibraryName(item.media.plexLibraryId) }}
						</span>
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
					If your remote libraries have finished syncing and comparing, this means you already own the matching content at the best available quality.
				</div>
			</div>
		</template>

		<MediaComparisonDetailsDialog />
		<DownloadConfirmation @download="downloadStore.downloadMedia($event)" />
	</q-page>
</template>

<script setup lang="ts">
import { get, set } from '@vueuse/core';
import { useSubscription } from '@vueuse/rxjs';
import {
	PlexMediaComparisonState,
	PlexMediaType,
	type DownloadMediaDTO,
	type PlexMediaSlimDTO,
} from '@dto';
import {
	useDialogStore,
	useDiscoverStore,
	useDownloadStore,
	useLibraryStore,
	useServerStore,
	useSettingsStore,
} from '@store';

const discoverStore = useDiscoverStore();
const downloadStore = useDownloadStore();
const dialogStore = useDialogStore();
const settingsStore = useSettingsStore();
const serverStore = useServerStore();
const libraryStore = useLibraryStore();
const router = useRouter();

const search = ref('');
const mediaTypeFilter = ref<'all' | 'movies' | 'tv'>('all');
const reasonFilter = ref<'all' | 'missing' | 'upgrades'>('all');

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

const missingCount = computed(() => discoverStore.items.filter((item) => missingStates.includes(item.comparisonState)).length);
const upgradeCount = computed(() => discoverStore.items.filter((item) => upgradeStates.includes(item.comparisonState)).length);

const filteredItems = computed(() => {
	const query = get(search).trim().toLocaleLowerCase();
	const typeFilter = get(mediaTypeFilter);
	const reason = get(reasonFilter);

	return discoverStore.items.filter((item) => {
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

function handleDownload(command: DownloadMediaDTO[]) {
	if (!command.length || !command.some((item) => item.mediaIds.length > 0)) {
		return;
	}

	const mediaType = command[0]?.type ?? PlexMediaType.Unknown;
	if (settingsStore.isConfirmationEnabled(mediaType)) {
		dialogStore.openMediaConfirmationDownloadDialog(command);
		return;
	}

	downloadStore.downloadMedia({
		customDestinationFolderPath: '',
		destinationFolderPathId: null,
		downloadMedias: command,
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
	useSubscription(discoverStore.refresh().subscribe());
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
  grid-template-columns: repeat(3, minmax(0, 1fr));
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
  padding: 0 12px;
}

.discover-source {
  padding: 4px 16px 0;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
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
}
</style>
