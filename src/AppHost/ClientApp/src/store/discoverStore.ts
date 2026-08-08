import Log from 'consola';
import Axios from 'axios';
import { acceptHMRUpdate, defineStore } from 'pinia';
import { reactive, toRefs } from 'vue';
import { concatMap, catchError, finalize, map, mergeMap, reduce, switchMap, tap } from 'rxjs/operators';
import { forkJoin, from, type Observable, of, range } from 'rxjs';
import { cloneDeep } from 'lodash-es';
import {
	PlexMediaComparisonState,
	PlexMediaType,
	VideoQuality,
	type PlexLibraryDTO,
	type PlexMediaQualityDTO,
	type PlexMediaSlimDTO,
	type PlexMediaStatisticsDTO,
} from '@dto';
import { plexMediaApi } from '@api';
import { getPlexMediaComparisonState } from '@composables';
import { useLibraryStore, useServerStore, useSettingsStore } from '@store';
import { getCachedValue, setCachedValue } from '@/utils/persistentCache';

export interface IDiscoverSource {
	media: PlexMediaSlimDTO;
	comparisonState: PlexMediaComparisonState;
}

export interface IDiscoverItem {
	key: string;
	media: PlexMediaSlimDTO;
	comparisonState: PlexMediaComparisonState;
	sources: IDiscoverSource[];
	wantedByArr: boolean;
	wantedBy: string[];
}

interface IDiscoverStoreState {
	items: IDiscoverItem[];
	loading: boolean;
	refreshing: boolean;
	errorMessage: string;
	completedQueries: number;
	totalQueries: number;
	lastUpdatedAt: number | null;
	loadedFromCache: boolean;
	arrConfigured: boolean;
	arrDataAvailable: boolean;
	arrWarnings: string[];
}

interface IDiscoverWantedItem {
	title: string;
	year: number;
	mediaType: string;
	source: string;
}

interface IDiscoverWantedResponse {
	radarrConfigured: boolean;
	sonarrConfigured: boolean;
	items: IDiscoverWantedItem[];
	warnings: string[];
}

interface IDiscoverFeedCache {
	items: IDiscoverItem[];
	arrDataAvailable: boolean;
	arrWarnings: string[];
	integrationSignature: string;
}

const PAGE_SIZE = 100;
const MAX_CONCURRENT_LIBRARY_QUERIES = 3;
const DISCOVER_CACHE_KEY = 'discover-feed-v2';
const DISCOVER_CACHE_TTL_MS = 30 * 60 * 1000;

const DISCOVER_STATES: readonly PlexMediaComparisonState[] = [
	PlexMediaComparisonState.Missing,
	PlexMediaComparisonState.HigherQuality,
	PlexMediaComparisonState.Partial,
	PlexMediaComparisonState.PartialAndHigherQuality,
];

const QUALITY_RANK: Record<VideoQuality, number> = {
	[VideoQuality.Unknown]: 0,
	[VideoQuality.None]: 0,
	[VideoQuality.SubSD144P]: 1,
	[VideoQuality.SubSDCIF]: 2,
	[VideoQuality.NHD]: 3,
	[VideoQuality.SD]: 4,
	[VideoQuality.DVD]: 5,
	[VideoQuality.HD]: 6,
	[VideoQuality.FullHD]: 7,
	[VideoQuality.QHD]: 8,
	[VideoQuality.UHD_4K]: 9,
	[VideoQuality.UHD_8K]: 10,
};

const STATE_RANK: Record<PlexMediaComparisonState, number> = {
	[PlexMediaComparisonState.Unknown]: 0,
	[PlexMediaComparisonState.NotCompared]: 0,
	[PlexMediaComparisonState.Owned]: 0,
	[PlexMediaComparisonState.Pending]: 0,
	[PlexMediaComparisonState.Missing]: 1,
	[PlexMediaComparisonState.Partial]: 2,
	[PlexMediaComparisonState.HigherQuality]: 3,
	[PlexMediaComparisonState.PartialAndHigherQuality]: 4,
};

export const useDiscoverStore = defineStore('discoverStore', () => {
	const defaultState: IDiscoverStoreState = {
		items: [],
		loading: false,
		refreshing: false,
		errorMessage: '',
		completedQueries: 0,
		totalQueries: 0,
		lastUpdatedAt: null,
		loadedFromCache: false,
		arrConfigured: false,
		arrDataAvailable: false,
		arrWarnings: [],
	};
	const state = reactive<IDiscoverStoreState>(cloneDeep(defaultState));

	const libraryStore = useLibraryStore();
	const serverStore = useServerStore();
	const settingsStore = useSettingsStore();

	const actions = {
		initialize(): Observable<IDiscoverItem[]> {
			state.arrConfigured = getters.isArrConfigured();
			const integrationSignature = getters.getIntegrationSignature();

			return from(getCachedValue<IDiscoverFeedCache>(DISCOVER_CACHE_KEY, DISCOVER_CACHE_TTL_MS, true)).pipe(
				switchMap((cached) => {
					const cacheMatchesIntegrations = cached?.value.integrationSignature === integrationSignature;
					if (cached && cacheMatchesIntegrations) {
						state.items = hydrateCachedItems(cached.value.items);
						state.arrDataAvailable = cached.value.arrDataAvailable;
						state.arrWarnings = cached.value.arrWarnings;
						state.lastUpdatedAt = cached.cachedAt;
						state.loadedFromCache = true;

						if (cached.isFresh) {
							return of(state.items);
						}
					}

					return actions.refresh();
				}),
			);
		},
		refresh(): Observable<IDiscoverItem[]> {
			state.loading = state.items.length === 0;
			state.refreshing = true;
			state.errorMessage = '';
			state.completedQueries = 0;
			state.arrWarnings = [];
			state.arrConfigured = getters.isArrConfigured();

			const remoteLibraries = getters.getRemoteDiscoverLibraries();
			const queries = remoteLibraries.flatMap((library) =>
				DISCOVER_STATES.map((comparisonState) => ({ library, comparisonState })),
			);

			state.totalQueries = queries.length;

			const plexItems$ = queries.length === 0
				? of([] as IDiscoverSource[])
				: from(queries).pipe(
						mergeMap(
							({ library, comparisonState }) =>
								loadLibraryComparisonState(library, comparisonState).pipe(
									catchError((error) => {
										Log.error('Discover query failed', {
											libraryId: library.id,
											comparisonState,
											error,
										});
										state.errorMessage = 'Some Plex libraries could not be loaded. The results below may be incomplete.';
										return of([] as IDiscoverSource[]);
									}),
									finalize(() => {
										state.completedQueries++;
									}),
								),
							MAX_CONCURRENT_LIBRARY_QUERIES,
						),
						reduce((allItems, queryItems) => allItems.concat(queryItems), [] as IDiscoverSource[]),
					);

			return forkJoin({
				plexItems: plexItems$,
				wanted: loadWantedItems(),
			}).pipe(
				map(({ plexItems, wanted }) => {
					state.arrDataAvailable = wanted.available;
					state.arrWarnings = wanted.response.warnings ?? [];
					return groupDiscoverItems(plexItems, wanted.response.items ?? []);
				}),
				map((items) => items.sort(sortDiscoverItems)),
				tap((items) => {
					state.items = items;
					state.lastUpdatedAt = Date.now();
					state.loadedFromCache = false;

					void setCachedValue<IDiscoverFeedCache>(DISCOVER_CACHE_KEY, {
						items,
						arrDataAvailable: state.arrDataAvailable,
						arrWarnings: [...state.arrWarnings],
						integrationSignature: getters.getIntegrationSignature(),
					});
				}),
				finalize(() => {
					state.loading = false;
					state.refreshing = false;
				}),
			);
		},
		selectBestSource(
			item: IDiscoverItem,
			requestedQualities: VideoQuality[] = [],
			requireOnline = false,
		): IDiscoverSource | null {
			return selectBestSource(item.sources, requestedQualities, requireOnline);
		},
		isSourceOnline(plexServerId: number): boolean {
			return isServerOnline(plexServerId);
		},
		getBestQuality(media: PlexMediaSlimDTO): VideoQuality {
			return getBestQuality(media);
		},
		getMatchingQualities(media: PlexMediaSlimDTO, requestedQualities: VideoQuality[]): PlexMediaQualityDTO[] {
			if (requestedQualities.length === 0) {
				return [];
			}

			return media.qualities.filter((quality) => requestedQualities.includes(quality.quality));
		},
		$reset() {
			Object.assign(state, cloneDeep(defaultState));
		},
	};

	function requestPage(
		library: PlexLibraryDTO,
		comparisonState: PlexMediaComparisonState,
		page: number,
	): Observable<PlexMediaStatisticsDTO | null> {
		return plexMediaApi.getAllMediaByTypeEndpoint({
			page,
			size: PAGE_SIZE,
			comparisonState,
			mediaType: library.type,
			plexLibraryId: library.id,
			filterOwnedMedia: false,
			filterOfflineMedia: false,
		}).pipe(
			map(({ isSuccess, value }) => isSuccess && value ? value : null),
		);
	}

	function loadLibraryComparisonState(
		library: PlexLibraryDTO,
		comparisonState: PlexMediaComparisonState,
	): Observable<IDiscoverSource[]> {
		return requestPage(library, comparisonState, 1).pipe(
			switchMap((firstPage) => {
				if (!firstPage) {
					return of([] as IDiscoverSource[]);
				}

				const firstItems = toDiscoverSources(firstPage.mediaList, comparisonState);
				const totalPages = Math.max(1, Math.ceil(firstPage.totalCount / PAGE_SIZE));
				if (totalPages === 1) {
					return of(firstItems);
				}

				return range(2, totalPages - 1).pipe(
					concatMap((page) => requestPage(library, comparisonState, page)),
					map((pageData) => pageData ? toDiscoverSources(pageData.mediaList, comparisonState) : []),
					reduce((items, pageItems) => items.concat(pageItems), firstItems),
				);
			}),
		);
	}

	function toDiscoverSources(
		mediaList: PlexMediaSlimDTO[],
		requestedState: PlexMediaComparisonState,
	): IDiscoverSource[] {
		return mediaList
			.filter((media) => getPlexMediaComparisonState(media) === requestedState)
			.map((media) => ({ media, comparisonState: requestedState }));
	}

	function loadWantedItems(): Observable<{
		available: boolean;
		response: IDiscoverWantedResponse;
	}> {
		const configured = getters.isArrConfigured();
		if (!configured) {
			return of({
				available: false,
				response: {
					radarrConfigured: false,
					sonarrConfigured: false,
					items: [],
					warnings: [],
				},
			});
		}

		return from(Axios.get<IDiscoverWantedResponse>('/api/Integration/Discover/Wanted')).pipe(
			map((response) => ({
				available: true,
				response: response.data,
			})),
			catchError((error) => {
				Log.warn('Failed to load Sonarr/Radarr wanted list for Discover', error);
				state.errorMessage = 'The Sonarr/Radarr wanted list could not be refreshed. Showing the Plex Discover feed instead.';
				return of({
					available: false,
					response: {
						radarrConfigured: settingsStore.integrationsSettings.radarr.isConfigured,
						sonarrConfigured: settingsStore.integrationsSettings.sonarr.isConfigured,
						items: [],
						warnings: ['Sonarr/Radarr wanted list is currently unavailable.'],
					},
				});
			}),
		);
	}

	function groupDiscoverItems(
		sources: IDiscoverSource[],
		wantedItems: IDiscoverWantedItem[],
	): IDiscoverItem[] {
		const grouped = new Map<string, IDiscoverSource[]>();

		for (const source of sources) {
			const key = getMediaIdentityKey(source.media);
			const existing = grouped.get(key) ?? [];
			existing.push(source);
			grouped.set(key, existing);
		}

		return [...grouped.entries()].flatMap(([key, groupedSources]) => {
			const fallback = groupedSources[0];
			if (!fallback) {
				return [];
			}

			const best = selectBestSource(groupedSources, [], false) ?? fallback;
			const wantedBy = getWantedBy(best.media, wantedItems);

			return [{
				key,
				media: best.media,
				comparisonState: getAggregateComparisonState(groupedSources),
				sources: groupedSources,
				wantedByArr: wantedBy.length > 0,
				wantedBy,
			}];
		});
	}

	function hydrateCachedItems(items: IDiscoverItem[]): IDiscoverItem[] {
		return items.map((item) => {
			const best = selectBestSource(item.sources, [], false) ?? item.sources[0];
			return {
				...item,
				media: best?.media ?? item.media,
				comparisonState: getAggregateComparisonState(item.sources),
			};
		});
	}

	function selectBestSource(
		sources: IDiscoverSource[],
		requestedQualities: VideoQuality[],
		requireOnline: boolean,
	): IDiscoverSource | null {
		const requested = [...new Set(requestedQualities)];
		let candidates = sources.filter((source) => isSourceAvailable(source.media));

		if (requireOnline) {
			candidates = candidates.filter((source) => isServerOnline(source.media.plexServerId));
		}

		if (requested.length > 0) {
			candidates = candidates.filter((source) =>
				requested.some((quality) => source.media.qualities.some((candidate) => candidate.quality === quality)),
			);
		}

		if (candidates.length === 0) {
			return null;
		}

		return [...candidates].sort((a, b) => {
			const onlineDifference = Number(isServerOnline(b.media.plexServerId)) - Number(isServerOnline(a.media.plexServerId));
			if (onlineDifference !== 0) {
				return onlineDifference;
			}

			const qualityDifference = getQualityScore(b.media) - getQualityScore(a.media);
			if (qualityDifference !== 0) {
				return qualityDifference;
			}

			const sizeDifference = b.media.mediaSize - a.media.mediaSize;
			if (sizeDifference !== 0) {
				return sizeDifference;
			}

			return b.media.addedAt.localeCompare(a.media.addedAt);
		})[0] ?? null;
	}

	function isSourceAvailable(media: PlexMediaSlimDTO): boolean {
		return media.id > 0 && (media.mediaSize > 0 || media.qualities.length > 0);
	}

	function isServerOnline(plexServerId: number): boolean {
		const server = serverStore.getServer(plexServerId);
		return serverStore.getServerStatus(plexServerId) || Boolean(server?.presence);
	}

	function getQualityScore(media: PlexMediaSlimDTO): number {
		return media.qualities.reduce((highest, item) => Math.max(highest, QUALITY_RANK[item.quality] ?? 0), 0);
	}

	function getBestQuality(media: PlexMediaSlimDTO): VideoQuality {
		if (!media.qualities.length) {
			return VideoQuality.Unknown;
		}

		const best = [...media.qualities]
			.sort((a, b) => (QUALITY_RANK[b.quality] ?? 0) - (QUALITY_RANK[a.quality] ?? 0))[0];

		return best?.quality ?? VideoQuality.Unknown;
	}

	function getAggregateComparisonState(sources: IDiscoverSource[]): PlexMediaComparisonState {
		return [...sources]
			.sort((a, b) => (STATE_RANK[b.comparisonState] ?? 0) - (STATE_RANK[a.comparisonState] ?? 0))[0]
			?.comparisonState ?? PlexMediaComparisonState.Unknown;
	}

	function getWantedBy(media: PlexMediaSlimDTO, wantedItems: IDiscoverWantedItem[]): string[] {
		const normalizedTitle = normalizeTitle(media.searchTitle || media.title);
		const mediaType = media.type === PlexMediaType.Movie ? 'Movie' : 'TvShow';

		return [...new Set(
			wantedItems
				.filter((wanted) => {
					if (wanted.mediaType !== mediaType) {
						return false;
					}

					if (normalizeTitle(wanted.title) !== normalizedTitle) {
						return false;
					}

					return wanted.year <= 0 || media.year <= 0 || wanted.year === media.year;
				})
				.map((wanted) => wanted.source),
		)];
	}

	function getMediaIdentityKey(media: PlexMediaSlimDTO): string {
		return [
			media.type,
			normalizeTitle(media.searchTitle || media.title),
			media.year || 0,
		].join(':');
	}

	function normalizeTitle(value: string): string {
		return value
			.normalize('NFKD')
			.toLocaleLowerCase()
			.replace(/[\u0300-\u036f]/g, '')
			.replace(/[^a-z0-9]/g, '');
	}

	function sortDiscoverItems(a: IDiscoverItem, b: IDiscoverItem): number {
		const aDate = Date.parse(a.media.addedAt || '') || 0;
		const bDate = Date.parse(b.media.addedAt || '') || 0;
		if (aDate !== bDate) {
			return bDate - aDate;
		}
		return a.media.title.localeCompare(b.media.title);
	}

	const getters = {
		getRemoteDiscoverLibraries(): PlexLibraryDTO[] {
			return libraryStore.getLibraries().filter((library) => {
				const server = serverStore.getServer(library.plexServerId);
				const supportedType = library.type === PlexMediaType.Movie || library.type === PlexMediaType.TvShow;
				return Boolean(
					supportedType
					&& library.isEnabled
					&& library.syncedAt
					&& server
					&& server.isEnabled
					&& !server.owned,
				);
			});
		},
		isArrConfigured(): boolean {
			return Boolean(
				settingsStore.integrationsSettings.radarr.isConfigured
				|| settingsStore.integrationsSettings.sonarr.isConfigured,
			);
		},
		getIntegrationSignature(): string {
			const radarr = settingsStore.integrationsSettings.radarr;
			const sonarr = settingsStore.integrationsSettings.sonarr;
			return [
				`radarr:${radarr.isConfigured}:${radarr.radarrBaseUrl || ''}`,
				`sonarr:${sonarr.isConfigured}:${sonarr.sonarrBaseUrl || ''}`,
			].join('|');
		},
	};

	return {
		...toRefs(state),
		...actions,
		...getters,
	};
});

if (import.meta.hot) {
	import.meta.hot.accept(acceptHMRUpdate(useDiscoverStore, import.meta.hot));
}
