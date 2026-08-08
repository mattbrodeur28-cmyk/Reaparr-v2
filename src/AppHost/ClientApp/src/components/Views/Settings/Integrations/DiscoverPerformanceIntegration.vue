<template>
	<QSection header="Performance & Cache">
		<QAlert type="info">
			V4 keeps Discover snapshots and resized Plex poster responses on the Reaparr server so every browser/device can reuse them.
		</QAlert>

		<HelpRow
			:col-label="3"
			label="Discover snapshot"
			text="The server snapshot is fresh for 15 minutes. Stale snapshots can render immediately while Reaparr refreshes them."
			title="Discover server snapshot">
			<div class="column q-gutter-xs">
				<q-chip
					:color="stats.snapshotExists ? 'positive' : 'grey-7'"
					text-color="white"
					icon="mdi-lightning-bolt">
					{{ stats.snapshotExists ? 'Cached' : 'Not cached' }}
				</q-chip>
				<div class="text-caption text-grey-6">
					{{ formatBytes(stats.snapshotBytes) }} • {{ formatAge(stats.snapshotAgeSeconds) }}
				</div>
			</div>
		</HelpRow>

		<HelpRow
			:col-label="3"
			label="Poster cache"
			text="Posters are cached after Plex has already resized/transcoded them for the requested dimensions. The server cache is shared by all devices."
			title="Server poster cache">
			<div class="column q-gutter-xs">
				<q-chip
					color="secondary"
					text-color="white"
					icon="mdi-image-multiple">
					{{ stats.posterCount }} posters
				</q-chip>
				<div class="text-caption text-grey-6">
					{{ formatBytes(stats.posterBytes) }}
				</div>
			</div>
		</HelpRow>

		<HelpRow
			:col-label="3"
			hide-label
			text="Clearing a cache does not remove media or change Plex, Sonarr, or Radarr."
			title="Cache maintenance">
			<div class="row q-gutter-sm">
				<BaseButton
					:loading="loading"
					icon="mdi-refresh"
					label="Refresh Stats"
					cy="refresh-performance-stats-button"
					@click="loadStats" />
				<BaseButton
					:loading="clearingSnapshot"
					color="warning"
					icon="mdi-database-remove"
					label="Clear Snapshot"
					cy="clear-discover-snapshot-button"
					@click="clearCache('snapshot')" />
				<BaseButton
					:loading="clearingPosters"
					color="warning"
					icon="mdi-image-remove"
					label="Clear Posters"
					cy="clear-poster-cache-button"
					@click="clearCache('posters')" />
			</div>
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

interface IPerformanceStats {
	snapshotExists: boolean;
	snapshotBytes: number;
	snapshotAgeSeconds: number;
	posterCount: number;
	posterBytes: number;
	posterCachePath: string;
	snapshotCachePath: string;
}

const stats = reactive<IPerformanceStats>({
	snapshotExists: false,
	snapshotBytes: 0,
	snapshotAgeSeconds: 0,
	posterCount: 0,
	posterBytes: 0,
	posterCachePath: '',
	snapshotCachePath: '',
});

const loading = ref(false);
const clearingSnapshot = ref(false);
const clearingPosters = ref(false);
const message = ref('');
const messageSuccess = ref(false);

function applyStats(next: IPerformanceStats) {
	Object.assign(stats, next);
}

async function loadStats() {
	loading.value = true;
	try {
		const response = await Axios.get<IPerformanceStats>('/api/Integration/Discover/Performance');
		applyStats(response.data);
	} catch {
		messageSuccess.value = false;
		message.value = 'Performance cache statistics could not be loaded.';
	} finally {
		loading.value = false;
	}
}

async function clearCache(scope: 'snapshot' | 'posters') {
	if (scope === 'snapshot') {
		clearingSnapshot.value = true;
	} else {
		clearingPosters.value = true;
	}

	message.value = '';
	try {
		const response = await Axios.delete<IPerformanceStats>(
			'/api/Integration/Discover/Performance/Cache',
			{ params: { scope } },
		);
		applyStats(response.data);
		messageSuccess.value = true;
		message.value = scope === 'snapshot'
			? 'Discover snapshot cache cleared.'
			: 'Server poster cache cleared.';
	} catch {
		messageSuccess.value = false;
		message.value = 'The selected cache could not be cleared.';
	} finally {
		clearingSnapshot.value = false;
		clearingPosters.value = false;
	}
}

function formatBytes(bytes: number): string {
	if (!Number.isFinite(bytes) || bytes <= 0) {
		return '0 B';
	}

	const units = ['B', 'KB', 'MB', 'GB'];
	const index = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1);
	const value = bytes / Math.pow(1024, index);
	return `${value.toFixed(index === 0 ? 0 : 1)} ${units[index]}`;
}

function formatAge(seconds: number): string {
	if (!Number.isFinite(seconds) || seconds <= 0) {
		return 'not built yet';
	}

	if (seconds < 60) {
		return 'less than a minute old';
	}

	const minutes = Math.round(seconds / 60);
	if (minutes < 60) {
		return `${minutes}m old`;
	}

	const hours = Math.round(minutes / 60);
	return `${hours}h old`;
}

onMounted(loadStats);
</script>
