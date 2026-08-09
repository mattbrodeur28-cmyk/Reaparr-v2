<template>
	<q-page class="discover-page-v5">
		<section class="discover-hero">
			<div class="discover-hero__glow discover-hero__glow--one" />
			<div class="discover-hero__glow discover-hero__glow--two" />

			<div class="discover-hero__content">
				<div class="discover-kicker">
					<q-icon name="mdi-compass-rose" />
					<span>Smart discovery</span>
				</div>

				<h1 class="discover-title">
					Discover what your library is missing.
				</h1>

				<p class="discover-subtitle">
					One title, one card, the best online source. Exact media IDs merge duplicate servers and the catalog loads in bounded windows instead of querying everything at once.
				</p>

				<div class="discover-hero__meta">
					<div class="discover-status-pill">
						<span
							class="discover-status-dot"
							:class="{ 'discover-status-dot--live': !discoverStore.refreshing }" />
						{{ cacheLabel || 'Ready' }}
					</div>
					<div class="discover-status-pill">
						<q-icon name="mdi-lightning-bolt-outline" />
						{{ snapshotPerformanceLabel }}
					</div>
					<div class="discover-status-pill">
						<q-icon name="mdi-database-eye-outline" />
						Server window {{ discoverStore.serverItemLimit }} / state
					</div>
				</div>
			</div>

			<div class="discover-hero__actions">
				<q-btn
					unelevated
					no-caps
					rounded
					class="v5-primary-action"
					icon="mdi-refresh"
					label="Refresh catalog"
					:loading="discoverStore.refreshing"
					data-cy="discover-refresh"
					@click="refresh" />
			</div>
		</section>

		<section class="discover-metrics">
			<div class="discover-metric">
				<div class="discover-metric__icon">
					<q-icon name="mdi-view-grid-outline" />
				</div>
				<div>
					<div class="discover-metric__value">
						{{ filteredItems.length }}
					</div>
					<div class="discover-metric__label">
						Loaded matches
					</div>
				</div>
			</div>

			<div class="discover-metric">
				<div class="discover-metric__icon discover-metric__icon--missing">
					<q-icon name="mdi-plus-circle-outline" />
				</div>
				<div>
					<div class="discover-metric__value">
						{{ missingCount }}
					</div>
					<div class="discover-metric__label">
						Missing / incomplete
					</div>
				</div>
			</div>

			<div class="discover-metric">
				<div class="discover-metric__icon discover-metric__icon--upgrade">
					<q-icon name="mdi-arrow-up-bold-circle-outline" />
				</div>
				<div>
					<div class="discover-metric__value">
						{{ upgradeCount }}
					</div>
					<div class="discover-metric__label">
						Upgrade opportunities
					</div>
				</div>
			</div>

			<div class="discover-metric">
				<div class="discover-metric__icon discover-metric__icon--identity">
					<q-icon name="mdi-fingerprint" />
				</div>
				<div>
					<div class="discover-metric__value">
						{{ exactIdentityCount }}
					</div>
					<div class="discover-metric__label">
						Exact identity groups
					</div>
				</div>
			</div>
		</section>

		<section class="discover-control-panel">
			<div class="discover-control-panel__top">
				<q-input
					v-model="search"
					borderless
					clearable
					debounce="150"
					placeholder="Search the loaded catalog"
					class="discover-search-v5"
					data-cy="discover-search">
					<template #prepend>
						<q-icon name="mdi-magnify" />
					</template>
				</q-input>

				<div class="discover-view-controls">
					<div class="discover-control-label">
						Per page
					</div>
					<q-select
						v-model="pageSize"
						dense
						borderless
						emit-value
						map-options
						:options="pageSizeOptions"
						class="discover-page-size"
						data-cy="discover-page-size" />
				</div>
			</div>

			<div class="discover-control-panel__bottom">
				<div class="discover-segment">
					<q-btn-toggle
						v-model="mediaTypeFilter"
						unelevated
						no-caps
						rounded
						toggle-color="primary"
						:options="mediaTypeOptions"
						data-cy="discover-media-type-filter" />
				</div>

				<div class="discover-segment">
					<q-btn-toggle
						v-model="reasonFilter"
						unelevated
						no-caps
						rounded
						toggle-color="primary"
						:options="reasonOptions"
						data-cy="discover-reason-filter" />
				</div>

				<div class="discover-wanted-toggle-v5">
					<q-toggle
						v-model="wantedOnly"
						color="primary"
						:disable="!discoverStore.arrConfigured"
						label="Only missing from Sonarr / Radarr" />
					<span>
						Upgrade opportunities stay visible.
					</span>
				</div>
			</div>
		</section>

		<div
			v-if="discoverStore.serverHasMore"
			class="discover-window-note">
			<div>
				<q-icon
					name="mdi-speedometer"
					size="20px" />
				<span>
					Reaparr stopped after {{ discoverStore.serverItemLimit }} items per comparison stream instead of querying the entire remote catalog.
				</span>
			</div>
			<q-btn
				flat
				no-caps
				rounded
				icon="mdi-plus"
				:label="`Load ${pageSize} more`"
				:loading="discoverStore.refreshing"
				@click="loadMore" />
		</div>

		<q-banner
			v-if="!discoverStore.arrConfigured"
			class="v5-banner v5-banner--info">
			Configure Radarr and/or Sonarr to enable wanted-only discovery.
		</q-banner>

		<q-banner
			v-else-if="!discoverStore.arrDataAvailable"
			class="v5-banner v5-banner--warning">
			The Sonarr/Radarr wanted list is unavailable, so Discover is showing the Plex missing and upgrade feed.
		</q-banner>

		<q-banner
			v-if="allWarnings.length"
			class="v5-banner v5-banner--warning">
			<div
				v-for="warning in allWarnings"
				:key="warning">
				{{ warning }}
			</div>
		</q-banner>

		<q-banner
			v-if="discoverStore.errorMessage"
			class="v5-banner v5-banner--warning">
			{{ discoverStore.errorMessage }}
		</q-banner>

		<section
			v-if="discoverStore.loading && !discoverStore.items.length"
			class="discover-skeleton-grid">
			<div
				v-for="index in 10"
				:key="index"
				class="discover-skeleton-card">
				<q-skeleton
					type="rect"
					class="discover-skeleton-poster" />
				<q-skeleton
					type="text"
					width="82%"
					class="q-mt-md" />
				<q-skeleton
					type="text"
					width="58%" />
			</div>
		</section>

		<template v-else>
			<div
				v-if="discoverStore.refreshing && discoverStore.items.length"
				class="discover-refresh-strip">
				<QSpinner
					size="18px"
					class="q-mr-sm" />
				Updating the catalog while your cached results stay usable.
			</div>

			<div
				v-if="filteredItems.length"
				class="discover-results-header">
				<div>
					<div class="discover-results-title">
						Your Discover feed
					</div>
					<div class="discover-results-subtitle">
						Showing {{ displayStart }}–{{ displayEnd }} of {{ filteredItems.length }} loaded matches
						<span v-if="discoverStore.serverHasMore">
							• more available on the server
						</span>
					</div>
				</div>
				<div class="discover-results-badges">
					<span class="discover-mini-badge">
						{{ mergedSourceCount }} duplicate sources merged
					</span>
					<span class="discover-mini-badge">
						{{ discoverStore.serverCacheStatus || 'Waiting' }} snapshot
					</span>
				</div>
			</div>

			<div
				v-if="pagedItems.length"
				class="discover-grid-v5"
				data-cy="discover-grid">
				<article
					v-for="item in pagedItems"
					:key="item.key"
					class="discover-card-v5">
					<div class="discover-card-v5__poster">
						<MediaPoster
							:media-item="item.media"
							@download="handleDownload($event, item)"
							@open-media-details="openMediaDetails" />

						<div class="discover-card-v5__badges">
							<span
								class="discover-reason-badge"
								:class="reasonClass(item.comparisonState)">
								<q-icon :name="reasonIcon(item.comparisonState)" />
								{{ reasonLabel(item.comparisonState, item.media.type) }}
							</span>

							<span
								v-if="item.sources.length > 1"
								class="discover-source-count">
								<q-icon name="mdi-server-network" />
								{{ item.sources.length }}
							</span>
						</div>
					</div>

					<div class="discover-card-v5__details">
						<div class="discover-card-v5__identity">
							<q-icon
								name="mdi-fingerprint"
								:class="{ 'text-warning': item.identityBasis === 'title-year' }" />
							{{ identityLabel(item) }}
						</div>

						<div
							v-if="item.media.type === PlexMediaType.TvShow"
							class="discover-tv-repair-cta">
							<q-btn
								flat
								dense
								no-caps
								rounded
								icon="mdi-format-list-checks"
								label="Review missing / upgrades"
								@click.stop="openTvFixPlan(item)" />
						</div>

						<div class="discover-card-v5__source">
							<div class="discover-card-v5__source-name">
								<span
									class="discover-online-dot"
									:class="{ 'discover-online-dot--online': discoverStore.isSourceOnline(item.media.plexServerId) }" />
								{{ serverStore.getServerName(item.media.plexServerId) }}
							</div>
							<div class="discover-card-v5__quality">
								{{ qualityLabel(item.media) }}
								<span v-if="item.wantedBy.length">
									• {{ item.wantedBy.join(' + ') }}
								</span>
							</div>
						</div>
					</div>
				</article>
			</div>

			<div
				v-if="filteredItems.length"
				class="discover-pagination">
				<div class="discover-pagination__summary">
					Page {{ page }} of {{ pageCount }}
				</div>
				<q-pagination
					v-model="page"
					:max="pageCount"
					:max-pages="7"
					direction-links
					boundary-links
					icon-first="mdi-page-first"
					icon-last="mdi-page-last"
					icon-prev="mdi-chevron-left"
					icon-next="mdi-chevron-right"
					color="primary" />
				<q-btn
					v-if="discoverStore.serverHasMore"
					flat
					no-caps
					rounded
					icon="mdi-database-plus-outline"
					:label="`Load ${pageSize} more from server`"
					:loading="discoverStore.refreshing"
					@click="loadMore" />
			</div>

			<div
				v-else
				class="discover-empty-v5">
				<div class="discover-empty-v5__icon">
					<q-icon
						name="mdi-check-decagram-outline"
						size="54px" />
				</div>
				<div class="discover-empty-v5__title">
					Nothing matches these filters
				</div>
				<div class="discover-empty-v5__copy">
					Try a different filter, disable the Sonarr/Radarr-only switch, or load more of the remote catalog.
				</div>
				<q-btn
					v-if="discoverStore.serverHasMore"
					unelevated
					no-caps
					rounded
					class="v5-primary-action q-mt-lg"
					icon="mdi-database-plus-outline"
					:label="`Load ${pageSize} more`"
					@click="loadMore" />
			</div>
		</template>

		<DiscoverTvFixDialog
			v-model="tvFixDialogOpen"
			:item="selectedTvItem"
			@queued="onTvFixQueued" />
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
import DiscoverTvFixDialog from '@/components/Views/Discover/DiscoverTvFixDialog.vue';

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
const pageSize = useLocalStorage<number>('reaparr-discover-page-size', 100);
const page = ref(1);
const tvFixDialogOpen = ref(false);
const selectedTvItem = ref<IDiscoverItem | null>(null);

const pageSizeOptions = [
	{ label: '25', value: 25 },
	{ label: '50', value: 50 },
	{ label: '100', value: 100 },
	{ label: '200', value: 200 },
];

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

	return discoverStore.items.flatMap((item) => {
		const onlineSources = item.sources.filter((source) =>
			discoverStore.isSourceOnline(source.media.plexServerId),
		);

		if (onlineSources.length === 0) {
			return [];
		}

		const bestSource = discoverStore.selectBestSource(
			{ ...item, sources: onlineSources },
			[],
			true,
		);

		if (!bestSource) {
			return [];
		}

		const liveItem: IDiscoverItem = {
			...item,
			media: bestSource.media,
			sources: onlineSources,
			comparisonState: discoverStore.getAggregateState(onlineSources),
		};

		if (get(applyWantedOnly) && !upgradeStates.includes(liveItem.comparisonState) && !liveItem.wantedByArr) {
			return [];
		}

		if (query && !liveItem.media.title.toLocaleLowerCase().includes(query)) {
			return [];
		}

		if (typeFilter === 'movies' && liveItem.media.type !== PlexMediaType.Movie) {
			return [];
		}
		if (typeFilter === 'tv' && liveItem.media.type !== PlexMediaType.TvShow) {
			return [];
		}
		if (reason === 'missing' && !missingStates.includes(liveItem.comparisonState)) {
			return [];
		}
		if (reason === 'upgrades' && !upgradeStates.includes(liveItem.comparisonState)) {
			return [];
		}

		return [liveItem];
	});
});

const pageCount = computed(() =>
	Math.max(1, Math.ceil(get(filteredItems).length / get(pageSize))),
);

const pagedItems = computed(() => {
	const size = get(pageSize);
	const currentPage = Math.min(get(page), get(pageCount));
	const start = (currentPage - 1) * size;
	return get(filteredItems).slice(start, start + size);
});

const displayStart = computed(() => {
	if (!get(filteredItems).length) {
		return 0;
	}
	return (Math.min(get(page), get(pageCount)) - 1) * get(pageSize) + 1;
});

const displayEnd = computed(() =>
	Math.min(get(displayStart) + get(pageSize) - 1, get(filteredItems).length),
);

const missingCount = computed(() =>
	get(filteredItems).filter((item) => missingStates.includes(item.comparisonState)).length,
);

const upgradeCount = computed(() =>
	get(filteredItems).filter((item) => upgradeStates.includes(item.comparisonState)).length,
);

const mergedSourceCount = computed(() =>
	discoverStore.items.reduce((count, item) => count + Math.max(0, item.sources.length - 1), 0),
);

const exactIdentityCount = computed(() =>
	discoverStore.items.filter((item) => item.identityBasis !== 'title-year').length,
);

const allWarnings = computed(() => [
	...discoverStore.arrWarnings,
	...discoverStore.identityWarnings,
	...discoverStore.snapshotWarnings,
]);

const snapshotPerformanceLabel = computed(() => {
	const buildMs = discoverStore.serverBuildMilliseconds;
	const ageSeconds = discoverStore.serverSnapshotAgeSeconds;
	const ageMinutes = Math.max(0, Math.round(ageSeconds / 60));

	if (!discoverStore.serverCacheStatus) {
		return 'Snapshot ready on first load';
	}

	if (discoverStore.serverCacheStatus === 'Rebuilt') {
		return `Built in ${buildMs} ms`;
	}

	if (ageMinutes < 1) {
		return 'Snapshot is less than a minute old';
	}

	return `${ageMinutes}m old • ${buildMs} ms build`;
});

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

watch(
	[search, mediaTypeFilter, reasonFilter, wantedOnly],
	() => {
		set(page, 1);
	},
);

watch(pageSize, (nextSize) => {
	set(page, 1);
	if (nextSize > discoverStore.serverItemLimit) {
		useSubscription(discoverStore.ensureItemLimit(nextSize).subscribe());
	}
});

watch(pageCount, (maxPage) => {
	if (get(page) > maxPage) {
		set(page, maxPage);
	}
});

function refresh() {
	const requestedLimit = Math.max(discoverStore.serverItemLimit, get(pageSize));
	useSubscription(discoverStore.refresh(true, requestedLimit).subscribe());
}

function loadMore() {
	useSubscription(discoverStore.loadMore(get(pageSize)).subscribe());
}

function reasonLabel(state: PlexMediaComparisonState, mediaType: PlexMediaType): string {
	if (mediaType === PlexMediaType.TvShow) {
		switch (state) {
			case PlexMediaComparisonState.Missing:
			case PlexMediaComparisonState.Partial:
				return 'Missing content';
			case PlexMediaComparisonState.HigherQuality:
				return 'Upgrades available';
			case PlexMediaComparisonState.PartialAndHigherQuality:
				return 'Missing + upgrades';
			default:
				return 'Review content';
		}
	}

	switch (state) {
		case PlexMediaComparisonState.Missing:
			return 'Missing';
		case PlexMediaComparisonState.HigherQuality:
			return 'Upgrade';
		case PlexMediaComparisonState.Partial:
			return 'Incomplete';
		case PlexMediaComparisonState.PartialAndHigherQuality:
			return 'Incomplete + upgrade';
		default:
			return 'Discover';
	}
}

function reasonClass(state: PlexMediaComparisonState): string {
	if (
		state === PlexMediaComparisonState.HigherQuality
		|| state === PlexMediaComparisonState.PartialAndHigherQuality
	) {
		return 'discover-reason-badge--upgrade';
	}
	return 'discover-reason-badge--missing';
}

function reasonIcon(state: PlexMediaComparisonState): string {
	if (
		state === PlexMediaComparisonState.HigherQuality
		|| state === PlexMediaComparisonState.PartialAndHigherQuality
	) {
		return 'mdi-arrow-up-bold-circle-outline';
	}
	return 'mdi-plus-circle-outline';
}

function identityLabel(item: IDiscoverItem): string {
	switch (item.identityBasis) {
		case 'tmdb':
			return `TMDB ${item.identityValue}`;
		case 'tvdb':
			return `TVDB ${item.identityValue}`;
		case 'imdb':
			return `IMDb ${item.identityValue}`;
		case 'plex':
			return 'Plex ID';
		case 'title-year':
		default:
			return 'Title + year fallback';
	}
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
	if (item.media.type === PlexMediaType.TvShow) {
		openTvFixPlan(item);
		return;
	}

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

function openTvFixPlan(item: IDiscoverItem) {
	selectedTvItem.value = item;
	tvFixDialogOpen.value = true;
}

function onTvFixQueued() {
	// Keep the current cached feed visible while Reaparr creates exact episode tasks.
	// V6 reconciliation will refresh owned Plex state after completed moves.
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
	useSubscription(discoverStore.initialize(get(pageSize)).subscribe());
});
</script>

<style lang="scss">
.discover-page-v5 {
  position: relative;
  z-index: 1;
  min-height: 100%;
  padding: 28px clamp(18px, 3vw, 46px) 56px;
}

.discover-hero {
  position: relative;
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 32px;
  overflow: hidden;
  padding: clamp(28px, 4vw, 52px);
  border: 1px solid var(--v5-border);
  border-radius: 30px;
  background:
    linear-gradient(135deg, rgba(255, 70, 98, 0.16), rgba(104, 72, 255, 0.08) 48%, rgba(10, 15, 28, 0.58)),
    var(--v5-surface);
  box-shadow: var(--v5-shadow-lg);
  backdrop-filter: blur(22px);
}

.discover-hero__glow {
  position: absolute;
  width: 340px;
  height: 340px;
  border-radius: 50%;
  filter: blur(65px);
  opacity: 0.28;
  pointer-events: none;
}

.discover-hero__glow--one {
  top: -190px;
  right: 8%;
  background: #ff3f6c;
}

.discover-hero__glow--two {
  bottom: -240px;
  left: 18%;
  background: #7357ff;
}

.discover-hero__content,
.discover-hero__actions {
  position: relative;
  z-index: 1;
}

.discover-hero__actions {
  display: flex;
  align-items: flex-start;
  justify-content: flex-end;
}

.discover-kicker {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 16px;
  color: var(--v5-accent-soft);
  font-size: 0.78rem;
  font-weight: 800;
  letter-spacing: 0.14em;
  text-transform: uppercase;
}

.discover-title {
  max-width: 860px;
  margin: 0;
  font-size: clamp(2.1rem, 4.3vw, 4.4rem);
  font-weight: 850;
  line-height: 0.98;
  letter-spacing: -0.055em;
}

.discover-subtitle {
  max-width: 820px;
  margin: 20px 0 0;
  color: var(--v5-text-muted);
  font-size: clamp(1rem, 1.4vw, 1.16rem);
  line-height: 1.65;
}

.discover-hero__meta {
  display: flex;
  flex-wrap: wrap;
  gap: 9px;
  margin-top: 24px;
}

.discover-status-pill,
.discover-mini-badge {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  padding: 7px 11px;
  border: 1px solid var(--v5-border);
  border-radius: 999px;
  background: var(--v5-surface-soft);
  color: var(--v5-text-muted);
  font-size: 0.78rem;
}

.discover-status-dot,
.discover-online-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: #f05f73;
  box-shadow: 0 0 0 4px rgba(240, 95, 115, 0.1);
}

.discover-status-dot--live,
.discover-online-dot--online {
  background: #5de6a2;
  box-shadow: 0 0 0 4px rgba(93, 230, 162, 0.1);
}

.v5-primary-action {
  min-height: 44px;
  padding: 0 18px;
  background: linear-gradient(135deg, #ff456a, #ff6d4a) !important;
  color: white !important;
  box-shadow: 0 12px 30px rgba(255, 69, 106, 0.24);
}

.discover-metrics {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
  margin-top: 16px;
}

.discover-metric {
  display: flex;
  align-items: center;
  gap: 14px;
  min-height: 92px;
  padding: 18px;
  border: 1px solid var(--v5-border);
  border-radius: 22px;
  background: var(--v5-surface);
  box-shadow: var(--v5-shadow-sm);
  backdrop-filter: blur(18px);
}

.discover-metric__icon {
  display: grid;
  width: 44px;
  height: 44px;
  flex: 0 0 44px;
  place-items: center;
  border-radius: 15px;
  background: rgba(111, 87, 255, 0.15);
  color: #a998ff;
  font-size: 1.25rem;
}

.discover-metric__icon--missing {
  background: rgba(255, 80, 112, 0.14);
  color: #ff7891;
}

.discover-metric__icon--upgrade {
  background: rgba(76, 220, 148, 0.14);
  color: #66e4a7;
}

.discover-metric__icon--identity {
  background: rgba(76, 181, 255, 0.14);
  color: #71c8ff;
}

.discover-metric__value {
  font-size: 1.65rem;
  font-weight: 820;
  line-height: 1;
}

.discover-metric__label {
  margin-top: 6px;
  color: var(--v5-text-muted);
  font-size: 0.8rem;
}

.discover-control-panel {
  position: relative;
  z-index: 2;
  isolation: isolate;
  box-sizing: border-box;
  width: 100%;
  margin-top: 16px;
  padding: 12px;
  border: 1px solid var(--v5-border);
  border-radius: 22px;
  background: var(--v5-surface-strong);
  box-shadow: var(--v5-shadow-md);
}

.discover-control-panel__top,
.discover-control-panel__bottom {
  display: flex;
  align-items: center;
  gap: 12px;
}

.discover-control-panel__bottom {
  flex-wrap: wrap;
  padding-top: 10px;
}

.discover-search-v5 {
  min-width: 220px;
  flex: 1 1 420px;
  min-height: 44px;
  padding: 0 14px;
  border-radius: 14px;
  background: var(--v5-surface-soft);
}

.discover-search-v5 .q-field__native,
.discover-search-v5 .q-field__input {
  min-width: 0;
  font-size: 0.95rem !important;
  font-weight: 500;
  line-height: 1.35 !important;
}

.discover-search-v5 .q-field__prepend,
.discover-search-v5 .q-field__append {
  font-size: 1.25rem;
}

.discover-view-controls {
  display: flex;
  align-items: center;
  gap: 8px;
  padding-left: 10px;
}

.discover-control-label {
  color: var(--v5-text-muted);
  font-size: 0.78rem;
  white-space: nowrap;
}

.discover-page-size {
  width: 104px;
  min-width: 104px;
  padding: 0 9px;
  border-radius: 12px;
  background: var(--v5-surface-soft);
}

.discover-page-size .q-field__native,
.discover-page-size .q-field__input {
  font-size: 0.84rem !important;
}

.discover-segment {
  padding: 3px;
  border-radius: 14px;
  background: var(--v5-surface-soft);
}

.discover-wanted-toggle-v5 {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-left: auto;
  color: var(--v5-text-muted);
  font-size: 0.76rem;
}

.discover-window-note,
.discover-refresh-strip {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 14px;
  margin-top: 12px;
  padding: 10px 14px;
  border: 1px solid rgba(105, 183, 255, 0.2);
  border-radius: 15px;
  background: rgba(70, 152, 255, 0.08);
  color: var(--v5-text-muted);
  font-size: 0.82rem;
}

.discover-window-note > div {
  display: flex;
  align-items: center;
  gap: 8px;
}

.v5-banner {
  margin-top: 12px;
  border-radius: 16px;
  border: 1px solid var(--v5-border);
}

.v5-banner--info {
  background: rgba(70, 152, 255, 0.11);
}

.v5-banner--warning {
  background: rgba(255, 174, 64, 0.12);
}

.discover-results-header {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 18px;
  margin: 30px 2px 14px;
}

.discover-results-title {
  font-size: 1.4rem;
  font-weight: 780;
}

.discover-results-subtitle {
  margin-top: 4px;
  color: var(--v5-text-muted);
  font-size: 0.82rem;
}

.discover-results-badges {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 7px;
}

.discover-grid-v5,
.discover-skeleton-grid {
  position: relative;
  z-index: 1;
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(215px, 1fr));
  gap: 18px;
}

.discover-card-v5,
.discover-skeleton-card {
  min-width: 0;
  overflow: hidden;
  border: 1px solid var(--v5-border);
  border-radius: 22px;
  background: var(--v5-surface);
  box-shadow: var(--v5-shadow-sm);
}

.discover-card-v5 {
  position: relative;
  z-index: 0;
  isolation: isolate;
  contain: paint;
  transition:
    transform 180ms ease,
    border-color 180ms ease,
    box-shadow 180ms ease;
}

.discover-card-v5:hover {
  z-index: 1;
  transform: translateY(-5px);
  border-color: rgba(255, 92, 119, 0.34);
  box-shadow: var(--v5-shadow-lg);
}

.discover-card-v5__poster {
  position: relative;
  overflow: hidden;
}

.discover-card-v5__poster .q-card,
.discover-card-v5__poster .media-poster {
  border-radius: 0 !important;
  box-shadow: none !important;
}

.discover-card-v5__badges {
  position: absolute;
  top: 10px;
  left: 10px;
  right: 10px;
  z-index: 2;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 7px;
  pointer-events: none;
}

.discover-reason-badge,
.discover-source-count {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  min-height: 27px;
  padding: 5px 9px;
  border: 1px solid rgba(255, 255, 255, 0.14);
  border-radius: 999px;
  color: white;
  font-size: 0.72rem;
  font-weight: 760;
  backdrop-filter: blur(16px);
}

.discover-reason-badge--missing {
  background: rgba(227, 55, 91, 0.82);
}

.discover-reason-badge--upgrade {
  background: rgba(35, 166, 105, 0.84);
}

.discover-source-count {
  margin-left: auto;
  background: rgba(15, 21, 34, 0.78);
}

.discover-card-v5__details {
  padding: 13px 14px 15px;
}

.discover-card-v5__identity {
  display: flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
  color: var(--v5-text-muted);
  font-size: 0.72rem;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.discover-card-v5__source {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 10px;
  margin-top: 10px;
}

.discover-card-v5__source-name {
  display: flex;
  align-items: center;
  gap: 7px;
  min-width: 0;
  font-size: 0.78rem;
  font-weight: 700;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.discover-card-v5__quality {
  flex: 0 0 auto;
  color: var(--v5-text-muted);
  font-size: 0.72rem;
}

.discover-online-dot {
  flex: 0 0 8px;
}

.discover-pagination {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 18px;
  margin-top: 28px;
  padding: 14px 16px;
  border: 1px solid var(--v5-border);
  border-radius: 18px;
  background: var(--v5-surface);
}

.discover-pagination__summary {
  color: var(--v5-text-muted);
  font-size: 0.82rem;
}

.discover-empty-v5 {
  display: flex;
  min-height: 380px;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  text-align: center;
}

.discover-empty-v5__icon {
  display: grid;
  width: 92px;
  height: 92px;
  place-items: center;
  border: 1px solid rgba(93, 230, 162, 0.24);
  border-radius: 30px;
  background: rgba(93, 230, 162, 0.08);
  color: #5de6a2;
}

.discover-empty-v5__title {
  margin-top: 22px;
  font-size: 1.45rem;
  font-weight: 800;
}

.discover-empty-v5__copy {
  max-width: 560px;
  margin-top: 8px;
  color: var(--v5-text-muted);
}

.discover-skeleton-grid {
  margin-top: 28px;
}

.discover-skeleton-card {
  padding-bottom: 14px;
}

.discover-skeleton-poster {
  aspect-ratio: 2 / 3;
  height: auto !important;
  border-radius: 0;
}

.discover-skeleton-card .q-skeleton:not(.discover-skeleton-poster) {
  margin-left: 14px;
  margin-right: 14px;
}

.discover-tv-repair-cta {
  margin-top: 9px;
}

.discover-tv-repair-cta .q-btn {
  width: 100%;
  min-height: 34px;
  justify-content: flex-start;
  border: 1px solid rgba(99, 190, 255, 0.14);
  background: rgba(99, 190, 255, 0.055);
  color: var(--v5-text-muted);
  font-size: 0.72rem;
}

.discover-tv-repair-cta .q-btn:hover {
  border-color: rgba(99, 190, 255, 0.3);
  color: var(--v5-text);
}

@media (max-width: 1050px) and (min-width: 761px) {
  .discover-control-panel__top {
    align-items: stretch;
  }

  .discover-control-panel__bottom {
    row-gap: 8px;
  }

  .discover-wanted-toggle-v5 {
    width: 100%;
    margin-left: 0;
  }
}

@media (max-width: 1100px) {
  .discover-metrics {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .discover-wanted-toggle-v5 {
    width: 100%;
    margin-left: 0;
  }
}

@media (max-width: 760px) {
  .discover-page-v5 {
    padding: 14px 12px 38px;
  }

  .discover-hero {
    grid-template-columns: 1fr;
    padding: 26px 22px;
    border-radius: 24px;
  }

  .discover-hero__actions {
    justify-content: flex-start;
  }

  .discover-title {
    font-size: clamp(2rem, 12vw, 3rem);
  }

  .discover-metrics {
    grid-template-columns: 1fr 1fr;
  }

  .discover-control-panel {
    z-index: 2;
  }

  .discover-control-panel__top,
  .discover-control-panel__bottom,
  .discover-results-header,
  .discover-pagination {
    align-items: stretch;
    flex-direction: column;
  }

  .discover-view-controls {
    justify-content: space-between;
    padding-left: 0;
  }

  .discover-results-badges {
    justify-content: flex-start;
  }

  .discover-grid-v5,
  .discover-skeleton-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 10px;
  }

  .discover-window-note {
    align-items: flex-start;
    flex-direction: column;
  }
}

@media (max-width: 470px) {
  .discover-metrics {
    grid-template-columns: 1fr;
  }

  .discover-grid-v5,
  .discover-skeleton-grid {
    grid-template-columns: 1fr;
  }
}

@media (prefers-reduced-motion: reduce) {
  .discover-card-v5 {
    transition: none;
  }

  .discover-card-v5:hover {
    transform: none;
  }
}
</style>
