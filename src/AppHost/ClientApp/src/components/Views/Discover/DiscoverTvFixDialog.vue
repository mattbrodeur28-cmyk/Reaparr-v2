<template>
	<q-dialog
		:model-value="modelValue"
		@update:model-value="emit('update:modelValue', $event)">
		<q-card class="discover-tv-fix-dialog">
			<q-card-section class="discover-tv-fix-dialog__header">
				<div class="discover-tv-fix-dialog__heading">
					<div class="discover-tv-fix-dialog__icon">
						<q-icon name="mdi-television-guide" />
					</div>
					<div>
						<div class="discover-tv-fix-dialog__eyebrow">
							Episode repair
						</div>
						<div class="discover-tv-fix-dialog__title">
							{{ plan?.title || item?.media.title || 'TV show' }}
						</div>
						<div class="discover-tv-fix-dialog__identity">
							{{ plan?.identity || identityLabel }}
						</div>
					</div>
				</div>

				<q-btn
					flat
					round
					icon="mdi-close"
					aria-label="Close"
					@click="emit('update:modelValue', false)" />
			</q-card-section>

			<q-separator />

			<q-card-section
				v-if="loading"
				class="discover-tv-fix-dialog__loading">
				<QSpinner size="38px" />
				<div>Comparing remote episodes with your owned Plex libraries…</div>
			</q-card-section>

			<template v-else-if="plan">
				<q-card-section class="discover-tv-fix-dialog__summary">
					<div class="discover-tv-fix-stat discover-tv-fix-stat--missing">
						<span>Missing</span>
						<strong>{{ plan.missingCount }}</strong>
					</div>
					<div class="discover-tv-fix-stat discover-tv-fix-stat--upgrade">
						<span>Upgrades</span>
						<strong>{{ plan.upgradeCount }}</strong>
					</div>
					<div class="discover-tv-fix-stat discover-tv-fix-stat--owned">
						<span>Owned</span>
						<strong>{{ plan.ownedCount }}</strong>
					</div>
				</q-card-section>

				<q-card-section class="discover-tv-fix-dialog__controls">
					<q-btn-toggle
						v-model="filter"
						unelevated
						no-caps
						rounded
						toggle-color="primary"
						:options="filterOptions" />

					<div class="discover-tv-fix-dialog__selection-actions">
						<q-btn
							flat
							dense
							no-caps
							label="Select missing"
							@click="selectByStatus('Missing')" />
						<q-btn
							flat
							dense
							no-caps
							label="Select upgrades"
							@click="selectByStatus('Upgrade')" />
						<q-btn
							flat
							dense
							no-caps
							label="Clear"
							@click="selectedKeys = []" />
					</div>
				</q-card-section>

				<q-banner
					v-if="plan.warnings.length"
					class="discover-tv-fix-dialog__warning">
					<div
						v-for="warning in plan.warnings"
						:key="warning">
						{{ warning }}
					</div>
				</q-banner>

				<div class="discover-tv-fix-dialog__episodes">
					<div
						v-for="episode in filteredEpisodes"
						:key="episodeKey(episode)"
						class="discover-tv-fix-row"
						:class="`discover-tv-fix-row--${episode.status.toLowerCase()}`">
						<q-checkbox
							v-if="episode.status !== 'Owned'"
							v-model="selectedKeys"
							:val="episodeKey(episode)"
							color="primary" />
						<div
							v-else
							class="discover-tv-fix-row__owned-check">
							<q-icon name="mdi-check-circle-outline" />
						</div>

						<div class="discover-tv-fix-row__episode">
							<div class="discover-tv-fix-row__number">
								S{{ pad(episode.seasonNumber) }}E{{ pad(episode.episodeNumber) }}
							</div>
							<div class="discover-tv-fix-row__title">
								{{ episode.title }}
							</div>
						</div>

						<div class="discover-tv-fix-row__status">
							<q-chip
								dense
								:color="statusColor(episode.status)"
								text-color="white"
								:icon="statusIcon(episode.status)">
								{{ episode.status }}
							</q-chip>
						</div>

						<div class="discover-tv-fix-row__quality">
							<template v-if="episode.status === 'Missing'">
								<span class="discover-tv-fix-row__quality-label">You have</span>
								<strong>—</strong>
								<q-icon name="mdi-arrow-right" />
								<strong>{{ qualityLabel(episode.remoteQuality) }}</strong>
							</template>

							<template v-else-if="episode.status === 'Upgrade'">
								<span class="discover-tv-fix-row__quality-label">You have</span>
								<strong>{{ qualityLabel(episode.ownedQuality) }}</strong>
								<q-icon name="mdi-arrow-right" />
								<strong class="text-positive">
									{{ qualityLabel(episode.remoteQuality) }}
								</strong>
							</template>

							<template v-else>
								<span class="discover-tv-fix-row__quality-label">Owned</span>
								<strong>{{ qualityLabel(episode.ownedQuality) }}</strong>
							</template>
						</div>

						<div class="discover-tv-fix-row__sources">
							{{ episode.sources.length }} source{{ episode.sources.length === 1 ? '' : 's' }}
						</div>
					</div>

					<div
						v-if="!filteredEpisodes.length"
						class="discover-tv-fix-dialog__empty">
						Nothing in this view.
					</div>
				</div>

				<q-separator />

				<QCardActions class="discover-tv-fix-dialog__footer">
					<div class="discover-tv-fix-dialog__footer-copy">
						<strong>{{ selectedKeys.length }}</strong>
						episode{{ selectedKeys.length === 1 ? '' : 's' }} selected.
						Only those exact episodes will be queued.
					</div>

					<div class="row q-gutter-sm">
						<q-btn
							flat
							no-caps
							label="Cancel"
							@click="emit('update:modelValue', false)" />
						<q-btn
							unelevated
							no-caps
							rounded
							color="primary"
							icon="mdi-download"
							:label="selectedKeys.length ? `Queue ${selectedKeys.length}` : 'Queue selected'"
							:disable="selectedKeys.length === 0"
							@click="queueSelected" />
					</div>
				</QCardActions>
			</template>

			<q-card-section
				v-else
				class="discover-tv-fix-dialog__empty">
				The episode comparison could not be built.
			</q-card-section>
		</q-card>
	</q-dialog>
</template>

<script setup lang="ts">
import Axios from 'axios';
import { useQuasar } from 'quasar';
import {
	PlexMediaType,
	VideoQuality,
	type DownloadMediaDTO,
} from '@dto';
import {
	useDialogStore,
	useDiscoverStore,
	useDownloadStore,
	useSettingsStore,
} from '@store';
import type { IDiscoverItem } from '@/store/discoverStore';

interface ITvFixSource {
	mediaId: number;
	plexServerId: number;
	plexLibraryId: number;
	quality: VideoQuality;
}

interface ITvFixEpisode {
	seasonNumber: number;
	episodeNumber: number;
	title: string;
	status: 'Missing' | 'Upgrade' | 'Owned';
	ownedQuality?: VideoQuality | null;
	remoteQuality: VideoQuality;
	sources: ITvFixSource[];
}

interface ITvFixPlan {
	title: string;
	identity: string;
	missingCount: number;
	upgradeCount: number;
	ownedCount: number;
	episodes: ITvFixEpisode[];
	warnings: string[];
}

const props = defineProps<{
	modelValue: boolean;
	item: IDiscoverItem | null;
}>();

const emit = defineEmits<{
	(e: 'update:modelValue', value: boolean): void;
	(e: 'queued'): void;
}>();

const discoverStore = useDiscoverStore();
const downloadStore = useDownloadStore();
const dialogStore = useDialogStore();
const settingsStore = useSettingsStore();
const $q = useQuasar();

const loading = ref(false);
const plan = ref<ITvFixPlan | null>(null);
const filter = ref<'fixes' | 'missing' | 'upgrades' | 'owned'>('fixes');
const selectedKeys = ref<string[]>([]);

const filterOptions = [
	{ label: 'Needs fixing', value: 'fixes' },
	{ label: 'Missing', value: 'missing' },
	{ label: 'Upgrades', value: 'upgrades' },
	{ label: 'Owned', value: 'owned' },
];

const identityLabel = computed(() => {
	const item = props.item;
	if (!item) {
		return '';
	}

	switch (item.identityBasis) {
		case 'tvdb':
			return `TVDB ${item.identityValue}`;
		case 'tmdb':
			return `TMDB ${item.identityValue}`;
		case 'imdb':
			return `IMDb ${item.identityValue}`;
		default:
			return item.identityValue;
	}
});

const filteredEpisodes = computed(() => {
	const episodes = plan.value?.episodes ?? [];

	switch (filter.value) {
		case 'missing':
			return episodes.filter((episode) => episode.status === 'Missing');
		case 'upgrades':
			return episodes.filter((episode) => episode.status === 'Upgrade');
		case 'owned':
			return episodes.filter((episode) => episode.status === 'Owned');
		case 'fixes':
		default:
			return episodes.filter((episode) => episode.status !== 'Owned');
	}
});

watch(
	() => props.modelValue,
	(isOpen) => {
		if (isOpen) {
			void loadPlan();
		}
	},
);

watch(
	() => props.item?.key,
	() => {
		if (props.modelValue) {
			void loadPlan();
		}
	},
);

async function loadPlan() {
	const item = props.item;
	if (!item || item.media.type !== PlexMediaType.TvShow) {
		plan.value = null;
		return;
	}

	loading.value = true;
	selectedKeys.value = [];
	filter.value = 'fixes';

	try {
		const response = await Axios.post<ITvFixPlan>(
			'/api/Integration/Discover/TvFixPlan',
			{
				remoteShowIds: item.sources
					.filter((source) => source.media.type === PlexMediaType.TvShow)
					.map((source) => source.media.id),
			},
		);

		plan.value = response.data;
	} catch {
		plan.value = null;
	} finally {
		loading.value = false;
	}
}

function episodeKey(episode: ITvFixEpisode): string {
	return `${episode.seasonNumber}:${episode.episodeNumber}`;
}

function selectByStatus(status: 'Missing' | 'Upgrade') {
	const keys = plan.value?.episodes
		.filter((episode) => episode.status === status)
		.map(episodeKey) ?? [];

	selectedKeys.value = [...new Set([...selectedKeys.value, ...keys])];
}

function pad(value: number): string {
	return value.toString().padStart(2, '0');
}

function statusColor(status: ITvFixEpisode['status']): string {
	switch (status) {
		case 'Upgrade':
			return 'positive';
		case 'Owned':
			return 'secondary';
		case 'Missing':
		default:
			return 'primary';
	}
}

function statusIcon(status: ITvFixEpisode['status']): string {
	switch (status) {
		case 'Upgrade':
			return 'mdi-arrow-up-bold-circle-outline';
		case 'Owned':
			return 'mdi-check-circle-outline';
		case 'Missing':
		default:
			return 'mdi-plus-circle-outline';
	}
}

function qualityRank(quality?: VideoQuality | null): number {
	switch (quality) {
		case VideoQuality.UHD_8K:
			return 10;
		case VideoQuality.UHD_4K:
			return 9;
		case VideoQuality.QHD:
			return 8;
		case VideoQuality.FullHD:
			return 7;
		case VideoQuality.HD:
			return 6;
		case VideoQuality.DVD:
			return 5;
		case VideoQuality.SD:
			return 4;
		case VideoQuality.NHD:
			return 3;
		case VideoQuality.SubSDCIF:
			return 2;
		case VideoQuality.SubSD144P:
			return 1;
		default:
			return 0;
	}
}

function qualityLabel(quality?: VideoQuality | null): string {
	switch (quality) {
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
		case VideoQuality.NHD:
			return 'NHD';
		default:
			return 'Unknown';
	}
}

function chooseSource(episode: ITvFixEpisode): ITvFixSource | null {
	const ranked = [...episode.sources]
		.sort((a, b) => qualityRank(b.quality) - qualityRank(a.quality));

	return ranked.find((source) => discoverStore.isSourceOnline(source.plexServerId))
		?? null;
}

function queueSelected() {
	const selected = new Set(selectedKeys.value);
	const commands: DownloadMediaDTO[] = [];
	let offlineCount = 0;

	for (const episode of plan.value?.episodes ?? []) {
		if (!selected.has(episodeKey(episode)) || episode.status === 'Owned') {
			continue;
		}

		const source = chooseSource(episode);
		if (!source) {
			offlineCount++;
			continue;
		}

		commands.push({
			type: PlexMediaType.Episode,
			mediaIds: [source.mediaId],
			plexLibraryId: source.plexLibraryId,
			plexServerId: source.plexServerId,
			qualities: [],
			keepCompletedInDownloadFolder: settingsStore.downloadManagerSettings.keepCompletedInDownloadFolder,
		});
	}

	if (!commands.length) {
		return;
	}

	if (settingsStore.isConfirmationEnabled(PlexMediaType.Episode)) {
		dialogStore.openMediaConfirmationDownloadDialog(commands);
	} else {
		downloadStore.downloadMedia({
			customDestinationFolderPath: '',
			destinationFolderPathId: null,
			downloadMedias: commands,
		});
	}

	if (offlineCount > 0) {
		$q.notify({
			type: 'warning',
			message: `${offlineCount} selected episode(s) did not have an online source and were not queued.`,
		});
	}

	emit('queued');
	emit('update:modelValue', false);
}
</script>

<style lang="scss">
.discover-tv-fix-dialog {
  width: min(1040px, calc(100vw - 30px));
  max-width: 1040px !important;
  max-height: min(880px, calc(100dvh - 30px));
  overflow: hidden;
  border: 1px solid var(--v5-border);
  border-radius: 24px !important;
  background: var(--v5-surface-strong) !important;
  box-shadow: var(--v5-shadow-lg);
}

.discover-tv-fix-dialog__header,
.discover-tv-fix-dialog__footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
}

.discover-tv-fix-dialog__heading {
  display: flex;
  min-width: 0;
  align-items: center;
  gap: 12px;
}

.discover-tv-fix-dialog__icon {
  display: grid;
  width: 46px;
  height: 46px;
  flex: 0 0 46px;
  place-items: center;
  border-radius: 15px;
  background: linear-gradient(135deg, rgba(255, 73, 108, 0.16), rgba(119, 92, 255, 0.12));
  color: var(--v5-accent-soft);
  font-size: 1.35rem;
}

.discover-tv-fix-dialog__eyebrow {
  color: var(--v5-text-muted);
  font-size: 0.67rem;
  font-weight: 800;
  letter-spacing: 0.12em;
  text-transform: uppercase;
}

.discover-tv-fix-dialog__title {
  margin-top: 2px;
  overflow: hidden;
  font-size: 1.35rem;
  font-weight: 820;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.discover-tv-fix-dialog__identity {
  margin-top: 2px;
  color: var(--v5-text-muted);
  font-size: 0.72rem;
}

.discover-tv-fix-dialog__loading,
.discover-tv-fix-dialog__empty {
  display: flex;
  min-height: 260px;
  align-items: center;
  justify-content: center;
  gap: 12px;
  color: var(--v5-text-muted);
  text-align: center;
}

.discover-tv-fix-dialog__summary {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 9px;
}

.discover-tv-fix-stat {
  padding: 11px 13px;
  border: 1px solid var(--v5-border);
  border-radius: 14px;
  background: var(--v5-surface-soft);
}

.discover-tv-fix-stat span {
  display: block;
  color: var(--v5-text-muted);
  font-size: 0.68rem;
  text-transform: uppercase;
  letter-spacing: 0.08em;
}

.discover-tv-fix-stat strong {
  display: block;
  margin-top: 2px;
  font-size: 1.4rem;
}

.discover-tv-fix-stat--missing strong {
  color: #ff7891;
}

.discover-tv-fix-stat--upgrade strong {
  color: #5de6a2;
}

.discover-tv-fix-dialog__controls {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding-top: 0;
}

.discover-tv-fix-dialog__selection-actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 2px;
}

.discover-tv-fix-dialog__warning {
  margin: 0 16px 10px;
  border-radius: 13px;
  background: rgba(255, 174, 64, 0.09);
}

.discover-tv-fix-dialog__episodes {
  max-height: min(54vh, 540px);
  overflow: auto;
  padding: 4px 16px 14px;
  overscroll-behavior: contain;
  -webkit-overflow-scrolling: touch;
}

.discover-tv-fix-row {
  display: grid;
  grid-template-columns: 32px minmax(180px, 1fr) auto minmax(190px, auto) 70px;
  align-items: center;
  gap: 10px;
  min-height: 64px;
  margin-top: 7px;
  padding: 9px 11px;
  border: 1px solid var(--v5-border);
  border-radius: 14px;
  background: var(--v5-surface-soft);
}

.discover-tv-fix-row--owned {
  opacity: 0.62;
}

.discover-tv-fix-row__owned-check {
  display: grid;
  place-items: center;
  color: #5de6a2;
  font-size: 1.2rem;
}

.discover-tv-fix-row__episode {
  min-width: 0;
}

.discover-tv-fix-row__number {
  color: var(--v5-text-muted);
  font-size: 0.68rem;
  font-weight: 800;
  letter-spacing: 0.08em;
}

.discover-tv-fix-row__title {
  margin-top: 2px;
  overflow: hidden;
  font-size: 0.8rem;
  font-weight: 720;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.discover-tv-fix-row__quality {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 7px;
  font-size: 0.75rem;
}

.discover-tv-fix-row__quality-label,
.discover-tv-fix-row__sources {
  color: var(--v5-text-muted);
  font-size: 0.68rem;
}

.discover-tv-fix-row__sources {
  text-align: right;
}

.discover-tv-fix-dialog__footer-copy {
  color: var(--v5-text-muted);
  font-size: 0.78rem;
}

@media (max-width: 720px) {
  .discover-tv-fix-dialog {
    width: calc(100vw - 14px);
    max-height: calc(100dvh - 14px);
    border-radius: 18px !important;
  }

  .discover-tv-fix-dialog__header {
    padding: 13px;
  }

  .discover-tv-fix-dialog__summary {
    padding: 10px 12px;
  }

  .discover-tv-fix-dialog__controls {
    align-items: stretch;
    flex-direction: column;
    padding: 0 12px 10px;
  }

  .discover-tv-fix-dialog__controls .q-btn-toggle {
    display: grid;
    grid-template-columns: repeat(2, 1fr);
  }

  .discover-tv-fix-dialog__selection-actions {
    justify-content: flex-start;
  }

  .discover-tv-fix-dialog__episodes {
    max-height: 55dvh;
    padding-right: 10px;
    padding-left: 10px;
  }

  .discover-tv-fix-row {
    grid-template-columns: 30px minmax(0, 1fr) auto;
    gap: 7px;
  }

  .discover-tv-fix-row__status {
    grid-column: 3;
    grid-row: 1;
  }

  .discover-tv-fix-row__quality {
    grid-column: 2 / 4;
    justify-content: flex-start;
  }

  .discover-tv-fix-row__sources {
    display: none;
  }

  .discover-tv-fix-dialog__footer {
    align-items: stretch;
    flex-direction: column;
    padding: 11px 12px max(11px, env(safe-area-inset-bottom));
  }

  .discover-tv-fix-dialog__footer .row {
    justify-content: flex-end;
  }
}
</style>
