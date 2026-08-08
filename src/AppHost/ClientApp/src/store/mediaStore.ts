import { acceptHMRUpdate, defineStore } from 'pinia';
import { reactive, toRefs } from 'vue';
import { from, type Observable, of } from 'rxjs';
import { map, take, catchError, switchMap } from 'rxjs/operators';
import type { PlexMediaComparisonDetailsDTO, PlexMediaType, PlexMediaDTO, BaseResultDTO } from '@dto';
import { StoreNames, type ISetupResult } from '@interfaces';
import { plexMediaApi } from '@api';
import { cloneDeep } from 'lodash-es';
import Log from 'consola';
import Axios from 'axios';
import { getCachedPosterBlob, setCachedPosterBlob } from '@/utils/persistentCache';

interface IMediaUrlStoreState {
	mediaUrls: IObjectUrl[];
}

interface IObjectUrl {
	cacheKey: string;
	url: string;
}

const POSTER_CACHE_TTL_MS = 30 * 24 * 60 * 60 * 1000;

export const useMediaStore = defineStore(StoreNames.MediaStore, () => {
	const defaultState: IMediaUrlStoreState = {
		mediaUrls: [],
	};

	const state = reactive<IMediaUrlStoreState>(cloneDeep(defaultState));
	const actions = {
		setup(): Observable<ISetupResult> {
			return of({ name: StoreNames.MediaStore, isSuccess: true }).pipe(take(1));
		},
		getMediaDataDetailById(mediaId: number, mediaType: PlexMediaType): Observable<PlexMediaDTO> {
			return plexMediaApi
				.getMediaDetailByIdEndpoint(mediaId, {
					type: mediaType,
				})
				.pipe(map((response) => response.value!));
		},
		getMediaComparisonDetails(mediaId: number, mediaType: PlexMediaType): Observable<PlexMediaComparisonDetailsDTO> {
			return plexMediaApi
				.getMediaComparisonDetailsEndpoint(mediaId, {
					type: mediaType,
				})
				.pipe(map((response) => response.value!));
		},
		getMediaThumbnailUrl(query: {
			plexServerId: number;
			plexKey: string;
			metaDataKey: number;
			height: number;
			width: number;
		}): Observable<string> {
			const cacheKey = [
				query.plexServerId,
				query.plexKey,
				query.metaDataKey,
				`${query.width}x${query.height}`,
			].join(':');

			const existing = state.mediaUrls.find((x) => x.cacheKey === cacheKey);
			if (existing) {
				return of(existing.url);
			}

			return from(getCachedPosterBlob(cacheKey, POSTER_CACHE_TTL_MS)).pipe(
				switchMap((cachedBlob) => {
					if (cachedBlob) {
						return of(actions.updateMediaUrl(cacheKey, cachedBlob));
					}

					return from(
						Axios.request<Blob | BaseResultDTO>({
							url: `/api/PlexMedia/thumbnail`,
							method: 'GET',
							params: query,
							responseType: 'blob',
						}),
					).pipe(
						map((res) => {
							if (res.status === 200) {
								const blob = res.data as Blob;
								void setCachedPosterBlob(cacheKey, blob);
								return actions.updateMediaUrl(cacheKey, blob);
							}

							Log.warn('Failed to get media thumbnail image', res);
							return '';
						}),
						catchError((error) => {
							Log.debug('Media thumbnail request failed', { query, error: error?.message || error });
							return of('');
						}),
					);
				}),
			);
		},
		updateMediaUrl(cacheKey: string, image: Blob): string {
			const index = state.mediaUrls.findIndex((x) => x.cacheKey === cacheKey);
			const mediaObject = Object.freeze({
				cacheKey,
				url: URL.createObjectURL(image),
			});

			if (index === -1) {
				state.mediaUrls.push(mediaObject);
			} else {
				const previous = state.mediaUrls[index];
				if (previous) {
					URL.revokeObjectURL(previous.url);
				}
				state.mediaUrls.splice(index, 1, mediaObject);
			}

			return mediaObject.url;
		},
		$reset() {
			state.mediaUrls.forEach((item) => URL.revokeObjectURL(item.url));
			Object.assign(state, cloneDeep(defaultState));
		},
	};

	const getters = {};
	return {
		...toRefs(state), ...actions, ...getters,
	};
});

if (import.meta.hot) {
	import.meta.hot.accept(acceptHMRUpdate(useMediaStore, import.meta.hot));
}
