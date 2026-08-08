<template>
	<QSection header="Library Reconciliation">
		<QAlert type="info">
			After Reaparr finishes moving a file, it can scan your owned Plex destination library and tell Radarr or Sonarr to rescan the exact title. This keeps Plex, Reaparr and the *arr missing state aligned.
		</QAlert>

		<HelpRow
			:col-label="3"
			label="Automatic reconciliation"
			text="Runs only after the download file has successfully reached its final destination."
			title="Automatic library reconciliation">
			<q-toggle
				v-model="settings.enabled"
				color="primary"
				label="Run after completed downloads" />
		</HelpRow>

		<HelpRow
			:col-label="3"
			label="Movie Plex library"
			text="Choose the movie library on an owned Plex server. Reaparr never sends a scan request to the remote source server."
			title="Destination movie library">
			<q-select
				v-model="settings.moviePlexLibraryId"
				:options="movieLibraryOptions"
				emit-value
				map-options
				clearable
				outlined
				dense
				option-label="label"
				option-value="value"
				placeholder="Select owned movie library"
				class="full-width" />
		</HelpRow>

		<HelpRow
			:col-label="3"
			label="TV Plex library"
			text="Choose the TV library on an owned Plex server."
			title="Destination TV library">
			<q-select
				v-model="settings.tvPlexLibraryId"
				:options="tvLibraryOptions"
				emit-value
				map-options
				clearable
				outlined
				dense
				option-label="label"
				option-value="value"
				placeholder="Select owned TV library"
				class="full-width" />
		</HelpRow>

		<HelpRow
			:col-label="3"
			label="Actions"
			text="Plex receives Scan Library Files. Reaparr then queues a forced sync of that owned library and clears the Discover snapshot."
			title="Plex and Reaparr actions">
			<div class="column q-gutter-sm">
				<q-toggle
					v-model="settings.refreshPlex"
					color="primary"
					label="Scan destination library in Plex" />
				<q-toggle
					v-model="settings.syncReaparrLibrary"
					color="primary"
					label="Queue Reaparr library sync after Plex scan" />
			</div>
		</HelpRow>

		<HelpRow
			:col-label="3"
			label="Radarr / Sonarr"
			text="Movie matching uses TMDB IDs. TV matching uses TVDB IDs. Reaparr resolves the *arr internal ID before queuing the rescan."
			title="Arr rescans">
			<div class="column q-gutter-sm">
				<q-toggle
					v-model="settings.rescanRadarr"
					color="primary"
					:disable="!status.radarrConfigured"
					:label="status.radarrConfigured ? 'Rescan matching movie in Radarr' : 'Radarr is not configured'" />
				<q-toggle
					v-model="settings.rescanSonarr"
					color="primary"
					:disable="!status.sonarrConfigured"
					:label="status.sonarrConfigured ? 'Rescan matching series in Sonarr' : 'Sonarr is not configured'" />
			</div>
		</HelpRow>

		<HelpRow
			:col-label="3"
			hide-label
			text="These buttons let you verify the destination Plex mappings without downloading anything."
			title="Manual Plex scan">
			<div class="row q-gutter-sm">
				<BaseButton
					:disabled="!settings.moviePlexLibraryId"
					:loading="refreshingMovie"
					icon="mdi-movie-open-refresh-outline"
					label="Scan Movies in Plex Now"
					@click="refreshPlexNow('movie')" />
				<BaseButton
					:disabled="!settings.tvPlexLibraryId"
					:loading="refreshingTv"
					icon="mdi-television-classic"
					label="Scan TV in Plex Now"
					@click="refreshPlexNow('tv')" />
			</div>
		</HelpRow>

		<HelpRow
			:col-label="3"
			hide-label
			text="Settings are stored in the Reaparr Config directory and survive container updates."
			title="Save reconciliation settings">
			<BaseButton
				:loading="saving"
				color="positive"
				icon="mdi-content-save"
				label="Save Reconciliation"
				@click="save" />
		</HelpRow>

		<QAlert
			v-if="message"
			:type="messageSuccess ? 'success' : 'error'">
			{{ message }}
		</QAlert>
	</QSection>
</template>

<script setup lang="ts">
import Axios from 'axios';
import { PlexMediaType } from '@dto';
import { useLibraryStore, useServerStore } from '@store';

interface ISettings {
	enabled: boolean;
	refreshPlex: boolean;
	moviePlexLibraryId: number | null;
	tvPlexLibraryId: number | null;
	syncReaparrLibrary: boolean;
	rescanRadarr: boolean;
	rescanSonarr: boolean;
}

interface IStatus {
	settings: ISettings;
	radarrConfigured: boolean;
	sonarrConfigured: boolean;
	updatedAt?: string | null;
}

interface IActionResponse {
	isSuccess: boolean;
	message: string;
}

const libraryStore = useLibraryStore();
const serverStore = useServerStore();

const settings = reactive<ISettings>({
	enabled: false,
	refreshPlex: true,
	moviePlexLibraryId: null,
	tvPlexLibraryId: null,
	syncReaparrLibrary: true,
	rescanRadarr: true,
	rescanSonarr: true,
});

const status = reactive<IStatus>({
	settings,
	radarrConfigured: false,
	sonarrConfigured: false,
	updatedAt: null,
});

const saving = ref(false);
const refreshingMovie = ref(false);
const refreshingTv = ref(false);
const message = ref('');
const messageSuccess = ref(false);

const ownedLibraries = computed(() =>
	libraryStore.getLibraries()
		.filter((library) => {
			const server = serverStore.getServer(library.plexServerId);
			return Boolean(library.isEnabled && server?.owned);
		})
		.map((library) => ({
			id: library.id,
			type: library.type,
			label: `${serverStore.getServerName(library.plexServerId)} • ${library.title}`,
		})),
);

const movieLibraryOptions = computed(() =>
	ownedLibraries.value
		.filter((library) => library.type === PlexMediaType.Movie)
		.map((library) => ({
			label: library.label,
			value: library.id,
		})),
);

const tvLibraryOptions = computed(() =>
	ownedLibraries.value
		.filter((library) => library.type === PlexMediaType.TvShow)
		.map((library) => ({
			label: library.label,
			value: library.id,
		})),
);

function applyStatus(next: IStatus) {
	Object.assign(settings, next.settings);
	status.radarrConfigured = next.radarrConfigured;
	status.sonarrConfigured = next.sonarrConfigured;
	status.updatedAt = next.updatedAt ?? null;
}

async function loadStatus() {
	try {
		const response = await Axios.get<IStatus>('/api/Integration/LibraryReconciliation');
		applyStatus(response.data);
	} catch {
		messageSuccess.value = false;
		message.value = 'Library reconciliation settings could not be loaded.';
	}
}

async function save() {
	saving.value = true;
	message.value = '';

	try {
		const response = await Axios.put<IStatus>(
			'/api/Integration/LibraryReconciliation',
			settings,
		);
		applyStatus(response.data);
		messageSuccess.value = true;
		message.value = 'Library reconciliation settings saved.';
	} catch {
		messageSuccess.value = false;
		message.value = 'Library reconciliation settings could not be saved.';
	} finally {
		saving.value = false;
	}
}

async function refreshPlexNow(type: 'movie' | 'tv') {
	const plexLibraryId = type === 'movie'
		? settings.moviePlexLibraryId
		: settings.tvPlexLibraryId;

	if (!plexLibraryId) {
		return;
	}

	if (type === 'movie') {
		refreshingMovie.value = true;
	} else {
		refreshingTv.value = true;
	}

	message.value = '';

	try {
		const response = await Axios.post<IActionResponse>(
			'/api/Integration/LibraryReconciliation/PlexRefresh',
			{
				plexLibraryId,
				syncReaparrLibrary: settings.syncReaparrLibrary,
			},
		);
		messageSuccess.value = response.data.isSuccess;
		message.value = response.data.message;
	} catch {
		messageSuccess.value = false;
		message.value = 'The Plex scan request failed.';
	} finally {
		refreshingMovie.value = false;
		refreshingTv.value = false;
	}
}

onMounted(loadStatus);
</script>
