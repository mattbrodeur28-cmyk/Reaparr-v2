import Log from 'consola';
import Axios from 'axios';
import { acceptHMRUpdate, defineStore } from 'pinia';
import { reactive, toRefs } from 'vue';
import { catchError, finalize, map, switchMap, tap } from 'rxjs/operators';
import { forkJoin, from, type Observable, of } from 'rxjs';
import { cloneDeep } from 'lodash-es';
import {
	PlexMediaComparisonState,
	PlexMediaType,
	VideoQuality,
	type PlexLibraryDTO,
	type PlexMediaQualityDTO,
	type PlexMediaSlimDTO,
} from '@dto';
import { useLibraryStore, useServerStore, useSettingsStore } from '@store';
import { getCachedValue, setCachedValue } from '@/utils/persistentCache';

export type DiscoverIdentityBasis = 'tmdb' | 'tvdb' | 'imdb' | 'plex' | 'title-year';

export interface IDiscoverMediaIdentity {
	mediaId: number;
	mediaType: PlexMediaType;
	plexServerId: number;
	plexLibraryId: number;
	plexApiRatingKey: number;
	plexGuid: string;
	tmdbId?: number | null;
	tvdbId?: number | null;
	imdbId?: string | null;
	tmdbEnriched: boolean;
}

export interface IDiscoverSource {
	media: PlexMediaSlimDTO;
	comparisonState: PlexMediaComparisonState;
	identity?: IDiscoverMediaIdentity;
}

export interface IDiscoverItem {
	key: string;
	media: PlexMediaSlimDTO;
	comparisonState: PlexMediaComparisonState;
	sources: IDiscoverSource[];
	wantedByArr: boolean;
	wantedBy: string[];
	identityBasis: DiscoverIdentityBasis;
	identityValue: string;
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
	identityWarnings: string[];
	tmdbConfigured: boolean;
	tmdbEnrichedCount: number;
	serverCacheStatus: string;
	serverSnapshotAgeSeconds: number;
	serverBuildMilliseconds: number;
	snapshotWarnings: string[];
	serverHasMore: boolean;
	serverItemLimit: number;
}

interface IDiscoverWantedItem {
	title: string;
	year: number;
	mediaType: string;
	source: string;
	tmdbId?: number | null;
	tvdbId?: number | null;
	imdbId?: string | null;
}

interface IDiscoverWantedResponse {
	radarrConfigured: boolean;
	sonarrConfigured: boolean;
	items: IDiscoverWantedItem[];
	warnings: string[];
}

interface IDiscoverIdentityResponse {
	tmdbConfigured: boolean;
	tmdbEnrichedCount: number;
	items: IDiscoverMediaIdentity[];
	warnings: string[];
}

interface IDiscoverMediaSnapshotSource {
	media: PlexMediaSlimDTO;
	comparisonState: PlexMediaComparisonState;
}

interface IDiscoverMediaSnapshotResponse {
	cacheStatus: string;
	isStale: boolean;
	ageSeconds: number;
	builtAtUtc: string;
	buildMilliseconds: number;
	queryCount: number;
	hasMore: boolean;
	itemLimitPerState: number;
	sources: IDiscoverMediaSnapshotSource[];
	warnings: string[];
}

interface IDiscoverFeedCache {
	items: IDiscoverItem[];
	arrDataAvailable: boolean;
	arrWarnings: string[];
	identityWarnings: string[];
	tmdbConfigured: boolean;
	tmdbEnrichedCount: number;
	serverCacheStatus: string;
	serverSnapshotAgeSeconds: number;
	serverBuildMilliseconds: number;
	snapshotWarnings: string[];
	serverHasMore: boolean;
	serverItemLimit: number;
	integrationSignature: string;
}

interface IIdentityDescriptor {
	exactKeys: string[];
	fallbackKey: string;
	basis: DiscoverIdentityBasis;
	value: string;
}

interface IGroupBucket {
	id: string;
	sources: IDiscoverSource[];
	fallbackKey: string;
}

const DISCOVER_CACHE_KEY = 'discover-feed-v5';
const DISCOVER_CACHE_TTL_MS = 5 * 60 * 1000;

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

const IDENTITY_BASIS_RANK: Record<DiscoverIdentityBasis, number> = {
	tmdb: 5,
	tvdb: 5,
	imdb: 4,
	plex: 3,
	'title-year': 1,
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
		identityWarnings: [],
		tmdbConfigured: false,
		tmdbEnrichedCount: 0,
		serverCacheStatus: '',
		serverSnapshotAgeSeconds: 0,
		serverBuildMilliseconds: 0,
		snapshotWarnings: [],
		serverHasMore: false,
		serverItemLimit: 100,
	};
	const state = reactive<IDiscoverStoreState>(cloneDeep(defaultState));

	const libraryStore = useLibraryStore();
	const serverStore = useServerStore();
	const settingsStore = useSettingsStore();

	const actions = {
		initialize(initialItemLimit = 100): Observable<IDiscoverItem[]> {
			state.arrConfigured = getters.isArrConfigured();
			const integrationSignature = getters.getIntegrationSignature();
			const requestedLimit = clampServerItemLimit(initialItemLimit);

			return from(getCachedValue<IDiscoverFeedCache>(DISCOVER_CACHE_KEY, DISCOVER_CACHE_TTL_MS, true)).pipe(
				switchMap((cached) => {
					const cacheMatchesIntegrations = cached?.value.integrationSignature === integrationSignature;
					if (cached && cacheMatchesIntegrations) {
						state.items = hydrateCachedItems(cached.value.items);
						state.arrDataAvailable = cached.value.arrDataAvailable;
						state.arrWarnings = cached.value.arrWarnings;
						state.identityWarnings = cached.value.identityWarnings ?? [];
						state.tmdbConfigured = cached.value.tmdbConfigured ?? false;
						state.tmdbEnrichedCount = cached.value.tmdbEnrichedCount ?? 0;
						state.serverCacheStatus = cached.value.serverCacheStatus ?? '';
						state.serverSnapshotAgeSeconds = cached.value.serverSnapshotAgeSeconds ?? 0;
						state.serverBuildMilliseconds = cached.value.serverBuildMilliseconds ?? 0;
						state.snapshotWarnings = cached.value.snapshotWarnings ?? [];
						state.serverHasMore = cached.value.serverHasMore ?? false;
						state.serverItemLimit = cached.value.serverItemLimit ?? requestedLimit;
						state.lastUpdatedAt = cached.cachedAt;
						state.loadedFromCache = true;

						if (cached.isFresh && state.serverItemLimit >= requestedLimit) {
							return of(state.items);
						}
					}

					return actions.refresh(false, Math.max(requestedLimit, state.serverItemLimit));
				}),
			);
		},
		refresh(forceServerRefresh = true, requestedItemLimit = state.serverItemLimit): Observable<IDiscoverItem[]> {
			state.loading = state.items.length === 0;
			state.refreshing = true;
			state.errorMessage = '';
			state.completedQueries = 0;
			state.totalQueries = 1;
			state.arrWarnings = [];
			state.identityWarnings = [];
			state.snapshotWarnings = [];
			state.arrConfigured = getters.isArrConfigured();
			state.serverItemLimit = clampServerItemLimit(requestedItemLimit);

			const remoteLibraries = getters.getRemoteDiscoverLibraries();

			return forkJoin({
				snapshot: loadMediaSnapshot(remoteLibraries, forceServerRefresh, state.serverItemLimit),
				wanted: loadWantedItems(),
			}).pipe(
				switchMap(({ snapshot, wanted }) => {
					state.completedQueries = 1;
					state.serverCacheStatus = snapshot.cacheStatus;
					state.serverSnapshotAgeSeconds = snapshot.ageSeconds;
					state.serverBuildMilliseconds = snapshot.buildMilliseconds;
					state.snapshotWarnings = snapshot.warnings ?? [];
					state.serverHasMore = snapshot.hasMore;
					state.serverItemLimit = snapshot.itemLimitPerState || state.serverItemLimit;

					const plexItems: IDiscoverSource[] = snapshot.sources
						.filter((source) => isServerOnline(source.media.plexServerId))
						.map((source) => ({
							media: source.media,
							comparisonState: source.comparisonState,
						}));

					return loadIdentities(plexItems).pipe(
						map((identity) => ({ plexItems, wanted, identity, snapshot })),
					);
				}),
				map(({ plexItems, wanted, identity, snapshot }) => {
					state.arrDataAvailable = wanted.available;
					state.arrWarnings = wanted.response.warnings ?? [];
					state.identityWarnings = identity.response.warnings ?? [];
					state.tmdbConfigured = identity.response.tmdbConfigured;
					state.tmdbEnrichedCount = identity.response.tmdbEnrichedCount;

					const enrichedSources = attachIdentities(plexItems, identity.response.items);
					const grouped = groupDiscoverItems(enrichedSources, wanted.response.items ?? []);

					if (snapshot.isStale && !forceServerRefresh) {
						queueBackgroundSnapshotRefresh();
					}

					return grouped;
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
						identityWarnings: [...state.identityWarnings],
						tmdbConfigured: state.tmdbConfigured,
						tmdbEnrichedCount: state.tmdbEnrichedCount,
						serverCacheStatus: state.serverCacheStatus,
						serverSnapshotAgeSeconds: state.serverSnapshotAgeSeconds,
						serverBuildMilliseconds: state.serverBuildMilliseconds,
						snapshotWarnings: [...state.snapshotWarnings],
						serverHasMore: state.serverHasMore,
						serverItemLimit: state.serverItemLimit,
						integrationSignature: getters.getIntegrationSignature(),
					});
				}),
				finalize(() => {
					state.loading = false;
					state.refreshing = false;
				}),
			);
		},
		loadMore(additionalItems = 100): Observable<IDiscoverItem[]> {
			const nextLimit = clampServerItemLimit(state.serverItemLimit + additionalItems);
			if (nextLimit <= state.serverItemLimit || !state.serverHasMore) {
				return of(state.items);
			}

			return actions.refresh(false, nextLimit);
		},
		ensureItemLimit(itemLimit: number): Observable<IDiscoverItem[]> {
			const nextLimit = clampServerItemLimit(itemLimit);
			if (nextLimit <= state.serverItemLimit) {
				return of(state.items);
			}

			return actions.refresh(false, nextLimit);
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
		getAggregateState(sources: IDiscoverSource[]): PlexMediaComparisonState {
			return getAggregateComparisonState(sources);
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

	let backgroundSnapshotRefreshQueued = false;

	function loadMediaSnapshot(
		libraries: PlexLibraryDTO[],
		forceRefresh: boolean,
		itemLimitPerState: number,
	): Observable<IDiscoverMediaSnapshotResponse> {
		return from(Axios.post<IDiscoverMediaSnapshotResponse>(
			'/api/Integration/Discover/MediaSnapshot',
			{
				libraries: libraries.map((library) => ({
					plexLibraryId: library.id,
					mediaType: library.type,
				})),
				forceRefresh,
				itemLimitPerState: clampServerItemLimit(itemLimitPerState),
			},
		)).pipe(
			map((response) => response.data),
			catchError((error) => {
				Log.error('Failed to load Discover server snapshot', error);
				state.errorMessage = 'The fast Discover snapshot could not be loaded.';
				return of({
					cacheStatus: 'Error',
					isStale: false,
					ageSeconds: 0,
					builtAtUtc: new Date().toISOString(),
					buildMilliseconds: 0,
					queryCount: 0,
					hasMore: false,
					itemLimitPerState: clampServerItemLimit(itemLimitPerState),
					sources: [],
					warnings: ['Discover server snapshot is unavailable.'],
				} satisfies IDiscoverMediaSnapshotResponse);
			}),
		);
	}

	function clampServerItemLimit(value: number): number {
		if (!Number.isFinite(value)) {
			return 100;
		}

		return Math.min(2000, Math.max(25, Math.round(value)));
	}

	function queueBackgroundSnapshotRefresh() {
		if (backgroundSnapshotRefreshQueued) {
			return;
		}

		backgroundSnapshotRefreshQueued = true;
		setTimeout(() => {
			actions.refresh(true, state.serverItemLimit)
				.pipe(finalize(() => {
					backgroundSnapshotRefreshQueued = false;
				}))
				.subscribe({
					error: (error) => {
						Log.debug('Background Discover snapshot refresh failed', error);
					},
				});
		}, 0);
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

	function loadIdentities(sources: IDiscoverSource[]): Observable<{
		available: boolean;
		response: IDiscoverIdentityResponse;
	}> {
		const requestMap = new Map<string, { mediaId: number; mediaType: PlexMediaType }>();
		for (const source of sources) {
			requestMap.set(
				`${source.media.type}:${source.media.id}`,
				{ mediaId: source.media.id, mediaType: source.media.type },
			);
		}
		const requestItems = [...requestMap.values()];

		if (requestItems.length === 0) {
			return of({
				available: true,
				response: {
					tmdbConfigured: false,
					tmdbEnrichedCount: 0,
					items: [],
					warnings: [],
				},
			});
		}

		return from(Axios.post<IDiscoverIdentityResponse>('/api/Integration/Discover/Identity', {
			items: requestItems,
			enrichWithTmdb: true,
		})).pipe(
			map((response) => ({
				available: true,
				response: response.data,
			})),
			catchError((error) => {
				Log.warn('Failed to load canonical Discover identities', error);
				return of({
					available: false,
					response: {
						tmdbConfigured: false,
						tmdbEnrichedCount: 0,
						items: [],
						warnings: ['Canonical identity lookup is unavailable. Discover is using title + year fallback matching.'],
					},
				});
			}),
		);
	}

	function attachIdentities(
		sources: IDiscoverSource[],
		identities: IDiscoverMediaIdentity[],
	): IDiscoverSource[] {
		const byMedia = new Map(
			identities.map((identity) => [
				`${identity.mediaType}:${identity.mediaId}`,
				identity,
			]),
		);

		return sources.map((source) => ({
			...source,
			identity: byMedia.get(`${source.media.type}:${source.media.id}`),
		}));
	}

	function groupDiscoverItems(
		sources: IDiscoverSource[],
		wantedItems: IDiscoverWantedItem[],
	): IDiscoverItem[] {
		const sortedSources = [...sources].sort((a, b) =>
			getIdentityConfidence(b) - getIdentityConfidence(a),
		);

		const groups = new Map<string, IGroupBucket>();
		const exactKeyToGroup = new Map<string, string>();
		const fallbackKeyToGroup = new Map<string, string | null>();
		let groupCounter = 0;

		for (const source of sortedSources) {
			const descriptor = describeIdentity(source);
			let groupId: string | undefined;

			for (const exactKey of descriptor.exactKeys) {
				const existing = exactKeyToGroup.get(exactKey);
				if (existing) {
					groupId = existing;
					break;
				}
			}

			if (!groupId && descriptor.exactKeys.length === 0) {
				const fallbackMatch = fallbackKeyToGroup.get(descriptor.fallbackKey);
				if (fallbackMatch) {
					groupId = fallbackMatch;
				}
			}

			if (!groupId) {
				groupId = `group-${groupCounter++}`;
				groups.set(groupId, {
					id: groupId,
					sources: [],
					fallbackKey: descriptor.fallbackKey,
				});

				const existingFallback = fallbackKeyToGroup.get(descriptor.fallbackKey);
				if (existingFallback === undefined) {
					fallbackKeyToGroup.set(descriptor.fallbackKey, groupId);
				} else if (existingFallback !== groupId) {
					fallbackKeyToGroup.set(descriptor.fallbackKey, null);
				}
			}

			const group = groups.get(groupId);
			if (!group) {
				continue;
			}

			group.sources.push(source);

			for (const exactKey of descriptor.exactKeys) {
				exactKeyToGroup.set(exactKey, groupId);
			}
		}

		return [...groups.values()].flatMap((group) => {
			const fallback = group.sources[0];
			if (!fallback) {
				return [];
			}

			const best = selectBestSource(group.sources, [], false) ?? fallback;
			const descriptor = describeIdentity(best);
			const strongest = getStrongestGroupIdentity(group.sources) ?? descriptor;
			const wantedBy = getWantedBy(group.sources, wantedItems);

			return [{
				key: strongest.exactKeys[0] ?? strongest.fallbackKey,
				media: best.media,
				comparisonState: getAggregateComparisonState(group.sources),
				sources: group.sources,
				wantedByArr: wantedBy.length > 0,
				wantedBy,
				identityBasis: strongest.basis,
				identityValue: strongest.value,
			}];
		});
	}

	function getStrongestGroupIdentity(sources: IDiscoverSource[]): IIdentityDescriptor | null {
		const descriptors = sources
			.map(describeIdentity)
			.sort((a, b) => IDENTITY_BASIS_RANK[b.basis] - IDENTITY_BASIS_RANK[a.basis]);

		return descriptors[0] ?? null;
	}

	function getIdentityConfidence(source: IDiscoverSource): number {
		return IDENTITY_BASIS_RANK[describeIdentity(source).basis];
	}

	function describeIdentity(source: IDiscoverSource): IIdentityDescriptor {
		const media = source.media;
		const identity = source.identity;
		const fallbackKey = getFallbackIdentityKey(media);
		const exactKeys: string[] = [];

		if (media.type === PlexMediaType.Movie) {
			if (identity?.tmdbId) {
				exactKeys.push(`movie:tmdb:${identity.tmdbId}`);
			}
			if (identity?.imdbId) {
				exactKeys.push(`movie:imdb:${normalizeExternalId(identity.imdbId)}`);
			}
			if (identity?.plexGuid) {
				exactKeys.push(`movie:plex:${identity.plexGuid.trim().toLocaleLowerCase()}`);
			}

			if (identity?.tmdbId) {
				return {
					exactKeys,
					fallbackKey,
					basis: 'tmdb',
					value: identity.tmdbId.toString(),
				};
			}
			if (identity?.imdbId) {
				return {
					exactKeys,
					fallbackKey,
					basis: 'imdb',
					value: identity.imdbId,
				};
			}
		} else if (media.type === PlexMediaType.TvShow) {
			if (identity?.tvdbId) {
				exactKeys.push(`tv:tvdb:${identity.tvdbId}`);
			}
			if (identity?.tmdbId) {
				exactKeys.push(`tv:tmdb:${identity.tmdbId}`);
			}
			if (identity?.imdbId) {
				exactKeys.push(`tv:imdb:${normalizeExternalId(identity.imdbId)}`);
			}
			if (identity?.plexGuid) {
				exactKeys.push(`tv:plex:${identity.plexGuid.trim().toLocaleLowerCase()}`);
			}

			if (identity?.tvdbId) {
				return {
					exactKeys,
					fallbackKey,
					basis: 'tvdb',
					value: identity.tvdbId.toString(),
				};
			}
			if (identity?.tmdbId) {
				return {
					exactKeys,
					fallbackKey,
					basis: 'tmdb',
					value: identity.tmdbId.toString(),
				};
			}
			if (identity?.imdbId) {
				return {
					exactKeys,
					fallbackKey,
					basis: 'imdb',
					value: identity.imdbId,
				};
			}
		}

		if (identity?.plexGuid) {
			return {
				exactKeys,
				fallbackKey,
				basis: 'plex',
				value: 'Plex GUID',
			};
		}

		return {
			exactKeys: [],
			fallbackKey,
			basis: 'title-year',
			value: `${media.title} (${media.year || 'unknown'})`,
		};
	}

	function getWantedBy(
		sources: IDiscoverSource[],
		wantedItems: IDiscoverWantedItem[],
	): string[] {
		const sourcesByStrength = [...sources].sort((a, b) =>
			getIdentityConfidence(b) - getIdentityConfidence(a),
		);

		const matched = wantedItems.filter((wanted) =>
			sourcesByStrength.some((source) => isWantedMatch(source, wanted)),
		);

		return [...new Set(matched.map((wanted) => wanted.source))];
	}

	function isWantedMatch(source: IDiscoverSource, wanted: IDiscoverWantedItem): boolean {
		const media = source.media;
		const identity = source.identity;
		const wantedType = wanted.mediaType === 'Movie' ? PlexMediaType.Movie : PlexMediaType.TvShow;

		if (media.type !== wantedType) {
			return false;
		}

		if (media.type === PlexMediaType.Movie) {
			if (identity?.tmdbId && wanted.tmdbId) {
				return identity.tmdbId === wanted.tmdbId;
			}
			if (identity?.imdbId && wanted.imdbId) {
				return normalizeExternalId(identity.imdbId) === normalizeExternalId(wanted.imdbId);
			}
		} else {
			if (identity?.tvdbId && wanted.tvdbId) {
				return identity.tvdbId === wanted.tvdbId;
			}
			if (identity?.tmdbId && wanted.tmdbId) {
				return identity.tmdbId === wanted.tmdbId;
			}
			if (identity?.imdbId && wanted.imdbId) {
				return normalizeExternalId(identity.imdbId) === normalizeExternalId(wanted.imdbId);
			}
		}

		const sharedTmdbNamespace = Boolean(identity?.tmdbId && wanted.tmdbId);
		const sharedTvdbNamespace = Boolean(identity?.tvdbId && wanted.tvdbId);
		const sharedImdbNamespace = Boolean(identity?.imdbId && wanted.imdbId);

		if (sharedTmdbNamespace || sharedTvdbNamespace || sharedImdbNamespace) {
			return false;
		}

		const normalizedTitle = normalizeTitle(media.searchTitle || media.title);
		return normalizeTitle(wanted.title) === normalizedTitle
			&& (wanted.year <= 0 || media.year <= 0 || wanted.year === media.year);
	}

	function hydrateCachedItems(items: IDiscoverItem[]): IDiscoverItem[] {
		return items.flatMap((item) => {
			const onlineSources = item.sources.filter((source) =>
				isServerOnline(source.media.plexServerId),
			);

			if (onlineSources.length === 0) {
				return [];
			}

			const best = selectBestSource(onlineSources, [], true) ?? onlineSources[0];
			if (!best) {
				return [];
			}

			return [{
				...item,
				sources: onlineSources,
				media: best.media,
				comparisonState: getAggregateComparisonState(onlineSources),
			}];
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
		return serverStore.getServerStatus(plexServerId);
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

	function getFallbackIdentityKey(media: PlexMediaSlimDTO): string {
		return [
			media.type,
			'title',
			normalizeTitle(media.searchTitle || media.title),
			media.year || 0,
		].join(':');
	}

	function normalizeExternalId(value: string): string {
		return value.trim().toLocaleLowerCase();
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
					&& !server.owned
					&& isServerOnline(library.plexServerId),
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
				'v5',
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
