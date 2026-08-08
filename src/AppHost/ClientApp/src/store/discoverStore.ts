import Log from 'consola';
import { acceptHMRUpdate, defineStore } from 'pinia';
import { reactive, toRefs } from 'vue';
import { concatMap, catchError, finalize, map, mergeMap, reduce, switchMap, tap } from 'rxjs/operators';
import { from, type Observable, of, range } from 'rxjs';
import { cloneDeep } from 'lodash-es';
import {
	PlexMediaComparisonState,
	PlexMediaType,
	type PlexLibraryDTO,
	type PlexMediaSlimDTO,
	type PlexMediaStatisticsDTO,
} from '@dto';
import { plexMediaApi } from '@api';
import { getPlexMediaComparisonState } from '@composables';
import { useLibraryStore, useServerStore, useSettingsStore } from '@store';

export interface IDiscoverItem {
	media: PlexMediaSlimDTO;
	comparisonState: PlexMediaComparisonState;
}

interface IDiscoverStoreState {
	items: IDiscoverItem[];
	loading: boolean;
	errorMessage: string;
	completedQueries: number;
	totalQueries: number;
}

const PAGE_SIZE = 100;
const MAX_CONCURRENT_LIBRARY_QUERIES = 3;

const DISCOVER_STATES: readonly PlexMediaComparisonState[] = [
	PlexMediaComparisonState.Missing,
	PlexMediaComparisonState.HigherQuality,
	PlexMediaComparisonState.Partial,
	PlexMediaComparisonState.PartialAndHigherQuality,
];

export const useDiscoverStore = defineStore('discoverStore', () => {
	const defaultState: IDiscoverStoreState = {
		items: [],
		loading: false,
		errorMessage: '',
		completedQueries: 0,
		totalQueries: 0,
	};

	const state = reactive<IDiscoverStoreState>(cloneDeep(defaultState));

	const libraryStore = useLibraryStore();
	const serverStore = useServerStore();
	const settingsStore = useSettingsStore();

	const actions = {
		refresh(): Observable<IDiscoverItem[]> {
			state.loading = true;
			state.errorMessage = '';
			state.completedQueries = 0;

			const remoteLibraries = getters.getRemoteDiscoverLibraries();
			const queries = remoteLibraries.flatMap((library) =>
				DISCOVER_STATES.map((comparisonState) => ({ library, comparisonState })),
			);

			state.totalQueries = queries.length;

			if (queries.length === 0) {
				state.items = [];
				state.loading = false;
				return of([]);
			}

			return from(queries).pipe(
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
								return of([] as IDiscoverItem[]);
							}),
							finalize(() => {
								state.completedQueries++;
							}),
						),
					MAX_CONCURRENT_LIBRARY_QUERIES,
				),
				reduce((allItems, queryItems) => allItems.concat(queryItems), [] as IDiscoverItem[]),
				map((items) => deduplicate(items)),
				map((items) => items.sort(sortDiscoverItems)),
				tap((items) => {
					state.items = items;
				}),
				finalize(() => {
					state.loading = false;
				}),
			);
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
			filterOfflineMedia: settingsStore.generalSettings.hideMediaFromOfflineServers,
		}).pipe(
			map(({ isSuccess, value }) => isSuccess && value ? value : null),
		);
	}

	function loadLibraryComparisonState(
		library: PlexLibraryDTO,
		comparisonState: PlexMediaComparisonState,
	): Observable<IDiscoverItem[]> {
		return requestPage(library, comparisonState, 1).pipe(
			switchMap((firstPage) => {
				if (!firstPage) {
					return of([] as IDiscoverItem[]);
				}

				const firstItems = toDiscoverItems(firstPage.mediaList, comparisonState);
				const totalPages = Math.max(1, Math.ceil(firstPage.totalCount / PAGE_SIZE));

				if (totalPages === 1) {
					return of(firstItems);
				}

				return range(2, totalPages - 1).pipe(
					concatMap((page) => requestPage(library, comparisonState, page)),
					map((pageData) => pageData ? toDiscoverItems(pageData.mediaList, comparisonState) : []),
					reduce((items, pageItems) => items.concat(pageItems), firstItems),
				);
			}),
		);
	}

	function toDiscoverItems(
		mediaList: PlexMediaSlimDTO[],
		requestedState: PlexMediaComparisonState,
	): IDiscoverItem[] {
		// We intentionally validate the state client-side as well. This keeps Discover correct
		// even on Reaparr builds where comparison-state filtering is not applied to every query path.
		return mediaList
			.filter((media) => getPlexMediaComparisonState(media) === requestedState)
			.map((media) => ({ media, comparisonState: requestedState }));
	}

	function deduplicate(items: IDiscoverItem[]): IDiscoverItem[] {
		const seen = new Set<string>();
		return items.filter((item) => {
			const key = `${item.media.plexServerId}:${item.media.plexLibraryId}:${item.media.id}`;
			if (seen.has(key)) {
				return false;
			}
			seen.add(key);
			return true;
		});
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
