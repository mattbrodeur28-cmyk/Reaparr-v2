<template>
	<QSection>
		<template #header>
			{{ $t('pages.settings.advanced.download-manager.header') }}
		</template>
		<HelpGroup>
			<!--	Max segmented downloads	-->
			<HelpRow
				:label="$t('help.settings.advanced.download-manager-section.download-segments.label')"
				:title="$t('help.settings.advanced.download-manager-section.download-segments.title')"
				:text="$t('help.settings.advanced.download-manager-section.download-segments.text')">
				<QSlider
					:model-value="settingsStore.downloadManagerSettings.downloadSegments"
					label
					label-always
					:min="1"
					:max="8"
					:step="1"
					class="q-mt-lg"
					@change="settingsStore.downloadManagerSettings.downloadSegments = $event" />
			</HelpRow>

			<!-- V8.3 mover concurrency -->
			<HelpRow
				label="Maximum simultaneous movers"
				title="Maximum simultaneous file movers"
				text="Limits how many completed files Reaparr copies into your destination libraries at the same time. Lower values reduce disk and memory/cache pressure.">
				<div class="column q-gutter-sm">
					<QSlider
						:model-value="moveStatus.maxConcurrentMovers"
						label
						label-always
						:min="1"
						:max="8"
						:step="1"
						class="q-mt-lg"
						@change="saveMoveLimit" />
					<div class="text-caption text-grey-6">
						Fair-share scheduling is always enabled. When multiple Plex servers have files waiting,
						Reaparr gives new mover slots to the server with the fewest active movers.
					</div>
				</div>
			</HelpRow>

			<HelpRow
				label="Mover runtime"
				title="Mover memory diagnostics"
				text="Shows the current mover count and memory measurements. Container file cache can become large during several simultaneous file copies even when the managed .NET heap is small.">
				<div class="column q-gutter-sm">
					<div class="row q-gutter-sm items-center">
						<q-chip
							color="primary"
							text-color="white"
							icon="mdi-swap-horizontal-bold">
							{{ moveStatus.activeMovers }} / {{ moveStatus.maxConcurrentMovers }} active
						</q-chip>
						<q-chip
							v-if="moveStatus.fairAcrossServers"
							color="positive"
							text-color="white"
							icon="mdi-scale-balance">
							Fair scheduling
						</q-chip>
					</div>

					<div class="text-caption text-grey-6">
						Process working set: {{ formatBytes(moveStatus.processWorkingSetBytes) }}
						•
						Managed heap: {{ formatBytes(moveStatus.managedHeapBytes) }}
					</div>
					<div
						v-if="moveStatus.containerMemoryBytes > 0"
						class="text-caption text-grey-6">
						Container memory: {{ formatBytes(moveStatus.containerMemoryBytes) }}
						•
						File cache: {{ formatBytes(moveStatus.containerFileCacheBytes) }}
					</div>

					<BaseButton
						:loading="moveStatusLoading"
						icon="mdi-refresh"
						label="Refresh mover stats"
						cy="refresh-mover-stats-button"
						@click="loadMoveStatus" />
				</div>
			</HelpRow>

			<QAlert
				v-if="moveMessage"
				:type="moveMessageSuccess ? 'success' : 'error'">
				{{ moveMessage }}
			</QAlert>

			<!-- Keep completed in download folder -->
			<HelpRow
				:label="$t('help.settings.advanced.download-manager-section.keep-in-downloads.label')"
				:title="$t('help.settings.advanced.download-manager-section.keep-in-downloads.title')"
				:text="$t('help.settings.advanced.download-manager-section.keep-in-downloads.text')">
				<QToggle
					:model-value="settingsStore.downloadManagerSettings.keepCompletedInDownloadFolder"
					@update:model-value="settingsStore.downloadManagerSettings.keepCompletedInDownloadFolder = $event" />
			</HelpRow>
		</HelpGroup>
	</QSection>
</template>

<script setup lang="ts">
import Axios from 'axios';
import { useSettingsStore } from '@store';

interface IMoveConcurrencyStatus {
	maxConcurrentMovers: number;
	fairAcrossServers: boolean;
	activeMovers: number;
	processWorkingSetBytes: number;
	managedHeapBytes: number;
	containerMemoryBytes: number;
	containerFileCacheBytes: number;
}

const settingsStore = useSettingsStore();

const moveStatus = reactive<IMoveConcurrencyStatus>({
	maxConcurrentMovers: 4,
	fairAcrossServers: true,
	activeMovers: 0,
	processWorkingSetBytes: 0,
	managedHeapBytes: 0,
	containerMemoryBytes: 0,
	containerFileCacheBytes: 0,
});

const moveStatusLoading = ref(false);
const moveMessage = ref('');
const moveMessageSuccess = ref(false);

function applyMoveStatus(next: IMoveConcurrencyStatus) {
	Object.assign(moveStatus, next);
}

async function loadMoveStatus() {
	moveStatusLoading.value = true;
	try {
		const response = await Axios.get<IMoveConcurrencyStatus>(
			'/api/Integration/Downloads/MoveConcurrency',
		);
		applyMoveStatus(response.data);
	} catch {
		moveMessageSuccess.value = false;
		moveMessage.value = 'Mover settings and runtime statistics could not be loaded.';
	} finally {
		moveStatusLoading.value = false;
	}
}

async function saveMoveLimit(value: number | null) {
	const maxConcurrentMovers = Number(value);
	if (!Number.isFinite(maxConcurrentMovers)) {
		return;
	}

	moveStatusLoading.value = true;
	moveMessage.value = '';

	try {
		const response = await Axios.put<IMoveConcurrencyStatus>(
			'/api/Integration/Downloads/MoveConcurrency',
			{ maxConcurrentMovers },
		);
		applyMoveStatus(response.data);
		moveMessageSuccess.value = true;
		moveMessage.value = `Maximum simultaneous movers set to ${response.data.maxConcurrentMovers}.`;
	} catch {
		moveMessageSuccess.value = false;
		moveMessage.value = 'Maximum simultaneous movers could not be saved.';
	} finally {
		moveStatusLoading.value = false;
	}
}

function formatBytes(bytes: number): string {
	if (!Number.isFinite(bytes) || bytes <= 0) {
		return '0 B';
	}

	const units = ['B', 'KB', 'MB', 'GB', 'TB'];
	const index = Math.min(
		Math.floor(Math.log(bytes) / Math.log(1024)),
		units.length - 1,
	);
	const value = bytes / Math.pow(1024, index);
	return `${value.toFixed(index === 0 ? 0 : 1)} ${units[index]}`;
}

onMounted(loadMoveStatus);
</script>
