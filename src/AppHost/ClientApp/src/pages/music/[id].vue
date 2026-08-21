<template>
	<QPage class="v7-page v7-media-page">
		<ThemeOnly>
			<section class="v7-page-hero v7-page-hero--compact">
				<div>
					<div class="v7-page-kicker">
						<q-icon name="mdi-music" />
						{{ $t('pages.music.music-id.kicker') }}
					</div>
					<h1 class="v7-page-title">
						{{ $t('pages.music.music-id.header') }}
					</h1>
					<p class="v7-page-subtitle">
						{{ $t('pages.music.music-id.sub-header', { artists: library?.artistCount ?? 0, tracks: library?.trackCount ?? 0 }) }}
					</p>
				</div>
			</section>
		</ThemeOnly>

		<section class="v7-media-overview-shell q-pa-md">
			<QLoading v-if="loading" />

			<QAlert
				v-else-if="error"
				type="error">
				{{ error }}
			</QAlert>

			<QAlert
				v-else-if="!library || library.artistCount === 0"
				type="info">
				{{ $t('pages.music.music-id.empty') }}
			</QAlert>

			<q-list
				v-else
				bordered
				separator>
				<q-expansion-item
					v-for="artist in library.artists"
					:key="artist.id"
					:label="artist.title"
					:caption="$t('pages.music.music-id.artist-caption', { albums: artist.albumCount, tracks: artist.trackCount })"
					icon="mdi-account-music"
					@show="loadArtist(artist.id)">
					<q-card>
						<q-card-section>
							<QLoading v-if="loadingArtistId === artist.id" />

							<div
								v-for="album in artist.albums"
								v-else
								:key="album.id"
								class="q-mb-md">
								<div class="text-subtitle1">
									{{ album.title }}
									<span
										v-if="album.year > 0"
										class="text-caption">{{ `(${album.year})` }}</span>
								</div>
								<q-markup-table
									dense
									flat>
									<thead>
										<tr>
											<th class="text-left">
												{{ $t('pages.music.music-id.column-track') }}
											</th>
											<th class="text-left">
												{{ $t('pages.music.music-id.column-title') }}
											</th>
											<th class="text-left">
												{{ $t('pages.music.music-id.column-quality') }}
											</th>
											<th class="text-left">
												{{ $t('pages.music.music-id.column-format') }}
											</th>
											<th class="text-right">
												{{ $t('pages.music.music-id.column-size') }}
											</th>
										</tr>
									</thead>
									<tbody>
										<tr
											v-for="track in album.tracks"
											:key="track.id">
											<td>{{ track.trackNumber }}</td>
											<td>{{ track.title }}</td>
											<td>{{ track.audioQuality }}</td>
											<td>{{ formatAudio(track) }}</td>
											<td class="text-right">
												<QFileSize :size="track.mediaSize" />
											</td>
										</tr>
									</tbody>
								</q-markup-table>
							</div>
						</q-card-section>
					</q-card>
				</q-expansion-item>
			</q-list>
		</section>
	</QPage>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { useRoute } from 'vue-router';
import { plexMediaApi } from '@api';
import type { MusicLibraryDTO, MusicTrackDTO } from '@dto';
import { definePageMeta } from '#imports';

definePageMeta({
	scrollToTop: false,
});

const route = useRoute();
const libraryId = +(route.params.id as string);

const library = ref<MusicLibraryDTO | null>(null);
const loading = ref(true);
const loadingArtistId = ref<number | null>(null);
const error = ref<string | null>(null);

plexMediaApi.getMusicLibraryEndpoint({ plexLibraryId: libraryId }).subscribe({
	next: (result) => {
		if (result.value) {
			library.value = result.value;
		} else {
			// A successful call with no payload means the request was rejected or the
			// library id does not exist. Surface it rather than showing an empty library.
			error.value = result.errors?.[0]?.message ?? 'Could not load this music library.';
		}
		loading.value = false;
	},
	error: (err: Error) => {
		error.value = err.message;
		loading.value = false;
	},
});

/**
 * Albums and tracks are fetched per artist so listing a large library stays one cheap query.
 * Already-loaded artists are not refetched when the panel is reopened.
 */
function loadArtist(artistId: number): void {
	const artist = library.value?.artists.find((x) => x.id === artistId);
	if (!artist || artist.albums.length > 0) {
		return;
	}

	loadingArtistId.value = artistId;
	plexMediaApi.getMusicLibraryEndpoint({ plexLibraryId: libraryId, artistId }).subscribe({
		next: (result) => {
			const loaded = result.value?.artists.find((x) => x.id === artistId);
			if (loaded) {
				artist.albums = loaded.albums;
			}
			loadingArtistId.value = null;
		},
		error: () => {
			loadingArtistId.value = null;
		},
	});
}

/**
 * Renders what Plex actually reported. Bitrate, sample rate and bit depth are null when unknown
 * rather than zero, so each is only shown when present.
 */
function formatAudio(track: MusicTrackDTO): string {
	const parts: string[] = [];
	if (track.format) {
		parts.push(track.format.toUpperCase());
	}
	if (track.bitDepth && track.sampleRate) {
		parts.push(`${track.bitDepth}bit/${(track.sampleRate / 1000).toFixed(1)}kHz`);
	}
	if (track.bitrate) {
		parts.push(`${track.bitrate} kbps`);
	}
	return parts.join(' · ') || '-';
}
</script>
