<template>
	<QSection header="Media Automation">
		<div class="v7-automation-intro">
			<div class="v7-automation-intro__icon">
				<q-icon name="mdi-robot-outline" />
			</div>
			<div>
				<div class="v7-automation-intro__title">
					Automatic missing repair & safe quality upgrades
				</div>
				<div class="v7-automation-intro__copy">
					Automation only works inside the bounded Discover snapshot. New automation starts conservative: Missing uses Dry Run by default, while Upgrades default to Dry Run and cannot delete an old file until the replacement is verified in your owned Plex library.
				</div>
			</div>
		</div>

		<q-banner
			v-if="!status.snapshotAvailable"
			class="v7-automation-banner v7-automation-banner--warning">
			<template #avatar>
				<q-icon name="mdi-database-alert-outline" />
			</template>
			No Discover snapshot is available. Open Discover and Refresh before running automation.
		</q-banner>

		<q-banner
			v-else-if="status.snapshotHasMore"
			class="v7-automation-banner">
			<template #avatar>
				<q-icon name="mdi-speedometer" />
			</template>
			Automation is intentionally bounded to the current Discover window of
			<strong>{{ status.snapshotItemLimitPerState }}</strong>
			items per comparison stream. It will not silently query the entire remote catalog.
		</q-banner>

		<div class="v7-automation-grid">
			<article class="v7-automation-card">
				<div class="v7-automation-card__header">
					<div class="v7-automation-card__icon v7-automation-card__icon--missing">
						<q-icon name="mdi-auto-fix" />
					</div>
					<div>
						<div class="v7-automation-card__eyebrow">
							Engine 01
						</div>
						<div class="v7-automation-card__title">
							Auto Fix Missing
						</div>
						<div class="v7-automation-card__subtitle">
							Match exact Radarr TMDB / Sonarr TVDB items to content available on remote Plex servers.
						</div>
					</div>
				</div>

				<div class="v7-automation-toggle-row">
					<div>
						<div class="text-weight-bold">
							Enable scheduled missing repair
						</div>
						<div class="text-caption text-grey-6">
							Runs only when the configured interval is due.
						</div>
					</div>
					<q-toggle
						v-model="settings.missing.enabled"
						color="primary" />
				</div>

				<div class="v7-automation-toggle-row">
					<div>
						<div class="text-weight-bold">
							Dry Run
						</div>
						<div class="text-caption text-grey-6">
							Build the plan without creating download tasks.
						</div>
					</div>
					<q-toggle
						v-model="settings.missing.dryRun"
						color="warning" />
				</div>

				<div class="v7-automation-options">
					<q-toggle
						v-model="settings.missing.movies"
						label="Movies"
						color="primary" />
					<q-toggle
						v-model="settings.missing.tvEpisodes"
						label="TV episodes"
						color="primary" />
				</div>

				<div class="v7-automation-form-grid">
					<q-select
						v-model="settings.missing.intervalMinutes"
						outlined
						dense
						emit-value
						map-options
						label="Run every"
						:options="intervalOptions" />
					<q-select
						v-model="settings.missing.maxItemsPerRun"
						outlined
						dense
						emit-value
						map-options
						label="Max per run"
						:options="maxItemOptions" />
				</div>

				<div class="v7-automation-card__actions">
					<BaseButton
						icon="mdi-flask-outline"
						label="Dry Run Missing Now"
						:loading="runningMissing"
						:disabled="!status.snapshotAvailable"
						@click="runNow('Missing', true)" />
					<BaseButton
						v-if="!settings.missing.dryRun"
						color="positive"
						icon="mdi-play"
						label="Run Missing Live Now"
						:loading="runningMissing"
						:disabled="!status.snapshotAvailable"
						@click="runNow('Missing', false)" />
				</div>
			</article>

			<article class="v7-automation-card v7-automation-card--upgrade">
				<div class="v7-automation-card__header">
					<div class="v7-automation-card__icon v7-automation-card__icon--upgrade">
						<q-icon name="mdi-creation-outline" />
					</div>
					<div>
						<div class="v7-automation-card__eyebrow">
							Engine 02
						</div>
						<div class="v7-automation-card__title">
							Auto Upgrade
						</div>
						<div class="v7-automation-card__subtitle">
							Replace owned media only when the remote source is genuinely higher quality.
						</div>
					</div>
				</div>

				<div class="v7-automation-toggle-row">
					<div>
						<div class="text-weight-bold">
							Enable scheduled upgrades
						</div>
						<div class="text-caption text-grey-6">
							Independent from Auto Fix Missing.
						</div>
					</div>
					<q-toggle
						v-model="settings.upgrades.enabled"
						color="primary" />
				</div>

				<q-select
					v-model="settings.upgrades.mode"
					outlined
					emit-value
					map-options
					label="Upgrade safety mode"
					:options="upgradeModeOptions"
					class="q-mt-md" />

				<div
					class="v7-upgrade-mode-description"
					:class="{ 'v7-upgrade-mode-description--danger': settings.upgrades.mode === 'AutomaticReplace' }">
					<q-icon :name="upgradeModeIcon" />
					<div>
						<div class="text-weight-bold">
							{{ upgradeModeTitle }}
						</div>
						<div class="text-caption">
							{{ upgradeModeDescription }}
						</div>
					</div>
				</div>

				<div class="v7-automation-options">
					<q-toggle
						v-model="settings.upgrades.movies"
						label="Movies"
						color="primary" />
					<q-toggle
						v-model="settings.upgrades.tvEpisodes"
						label="TV episodes"
						color="primary" />
				</div>

				<div class="v7-automation-form-grid">
					<q-select
						v-model="settings.upgrades.intervalMinutes"
						outlined
						dense
						emit-value
						map-options
						label="Run every"
						:options="upgradeIntervalOptions" />
					<q-select
						v-model="settings.upgrades.maxItemsPerRun"
						outlined
						dense
						emit-value
						map-options
						label="Max per run"
						:options="maxItemOptions" />
				</div>

				<div class="v7-automation-card__actions">
					<BaseButton
						icon="mdi-flask-outline"
						label="Dry Run Upgrades Now"
						:loading="runningUpgrades"
						:disabled="!status.snapshotAvailable"
						@click="runNow('Upgrades', true)" />
					<BaseButton
						v-if="settings.upgrades.mode !== 'DryRun'"
						color="positive"
						icon="mdi-play"
						label="Run Upgrade Mode Now"
						:loading="runningUpgrades"
						:disabled="!status.snapshotAvailable"
						@click="runNow('Upgrades', false)" />
				</div>
			</article>
		</div>

		<div class="v7-automation-savebar">
			<div>
				<div class="text-weight-bold">
					Automation settings
				</div>
				<div class="text-caption text-grey-6">
					Saved under Reaparr's Config directory; no database migration is required.
				</div>
			</div>
			<BaseButton
				color="positive"
				icon="mdi-content-save"
				label="Save Automation"
				:loading="saving"
				@click="save" />
		</div>

		<section
			v-if="activeStages.length"
			class="v7-automation-section">
			<div class="v7-automation-section__heading">
				<div>
					<div class="v7-automation-section__eyebrow">
						Protected replacement queue
					</div>
					<div class="v7-automation-section__title">
						Staged upgrades
					</div>
				</div>
				<BaseButton
					v-if="verifiedStages.length"
					color="negative"
					icon="mdi-delete-check-outline"
					label="Finalize All Verified"
					:loading="finalizing"
					@click="finalizeAll" />
			</div>

			<div class="v7-stage-list">
				<article
					v-for="stage in activeStages"
					:key="stage.id"
					class="v7-stage-row">
					<div class="v7-stage-row__main">
						<div class="v7-stage-row__title">
							{{ stage.title }}
						</div>
						<div class="v7-stage-row__meta">
							<span>{{ stage.existingQuality }} → {{ stage.targetQuality }}</span>
							<span>•</span>
							<span>{{ stage.mediaType }}</span>
							<span>•</span>
							<span>{{ stage.mode }}</span>
						</div>
						<div class="v7-stage-row__message">
							{{ stage.lastMessage }}
						</div>
					</div>

					<div class="v7-stage-row__side">
						<q-chip
							dense
							:color="stageColor(stage.status)"
							text-color="white"
							:icon="stageIcon(stage.status)">
							{{ stage.status }}
						</q-chip>

						<BaseButton
							v-if="stage.status === 'Verified'"
							color="negative"
							icon="mdi-delete-check-outline"
							label="Delete old file"
							:loading="finalizing"
							@click="finalizeStage(stage.id)" />

						<BaseButton
							v-if="stage.status === 'WaitingForReplacement' || stage.status === 'Verified'"
							flat
							icon="mdi-shield-off-outline"
							label="Disable deletion"
							@click="cancelStage(stage.id)" />
					</div>
				</article>
			</div>
		</section>

		<section class="v7-automation-section">
			<div class="v7-automation-section__heading">
				<div>
					<div class="v7-automation-section__eyebrow">
						Visibility before automation
					</div>
					<div class="v7-automation-section__title">
						Recent plans & runs
					</div>
				</div>
				<BaseButton
					flat
					icon="mdi-refresh"
					label="Refresh"
					:loading="loading"
					@click="loadStatus" />
			</div>

			<div
				v-if="recentRuns.length"
				class="v7-run-list">
				<article
					v-for="run in recentRuns"
					:key="run.id"
					class="v7-run-card">
					<div class="v7-run-card__header">
						<div>
							<div class="v7-run-card__title">
								{{ run.engine }}
								<span v-if="run.dryRun">Dry Run</span>
							</div>
							<div class="v7-run-card__meta">
								{{ formatDate(run.completedAtUtc) }}
							</div>
						</div>
						<div class="v7-run-card__stats">
							<span>{{ run.candidateCount }} candidates</span>
							<span>{{ run.queuedCount }} queued</span>
							<span>{{ run.skippedCount }} skipped</span>
						</div>
					</div>

					<div
						v-if="run.warnings.length"
						class="v7-run-card__warnings">
						<div
							v-for="warning in run.warnings"
							:key="warning">
							{{ warning }}
						</div>
					</div>

					<div
						v-if="run.candidates.length"
						class="v7-candidate-list">
						<div
							v-for="candidate in run.candidates"
							:key="candidate.key"
							class="v7-candidate">
							<div>
								<div class="v7-candidate__title">
									{{ candidate.title }}
								</div>
								<div class="v7-candidate__meta">
									{{ candidate.identity }} • {{ candidate.detail }}
								</div>
							</div>
							<div class="v7-candidate__quality">
								{{ candidate.sourceQuality }}
							</div>
						</div>
					</div>
				</article>
			</div>

			<div
				v-else
				class="v7-automation-empty">
				Run either engine in Dry Run mode to preview what Reaparr would do.
			</div>
		</section>

		<QAlert
			v-if="message"
			:type="messageSuccess ? 'success' : 'error'">
			{{ message }}
		</QAlert>
	</QSection>
</template>

<script setup lang="ts">
import Axios from 'axios';

type UpgradeMode
	= | 'DryRun'
		| 'DownloadOnly'
		| 'ReplaceAfterApproval'
		| 'AutomaticReplace';

interface IMissingSettings {
	enabled: boolean;
	dryRun: boolean;
	movies: boolean;
	tvEpisodes: boolean;
	intervalMinutes: number;
	maxItemsPerRun: number;
}

interface IUpgradeSettings {
	enabled: boolean;
	mode: UpgradeMode;
	movies: boolean;
	tvEpisodes: boolean;
	intervalMinutes: number;
	maxItemsPerRun: number;
}

interface IAutomationSettings {
	missing: IMissingSettings;
	upgrades: IUpgradeSettings;
}

interface ICandidate {
	key: string;
	engine: string;
	mediaType: string;
	title: string;
	identity: string;
	detail: string;
	sourceQuality: string;
	existingQuality: string;
	wouldDownload: boolean;
	wouldDeleteOldFile: boolean;
}

interface IRun {
	id: string;
	engine: string;
	dryRun: boolean;
	startedAtUtc: string;
	completedAtUtc: string;
	candidateCount: number;
	queuedCount: number;
	skippedCount: number;
	warnings: string[];
	candidates: ICandidate[];
}

interface IStage {
	id: string;
	key: string;
	mediaType: string;
	title: string;
	mode: UpgradeMode;
	status: string;
	existingQuality: string;
	targetQuality: string;
	queuedAtUtc: string;
	verifiedAtUtc?: string | null;
	deletedAtUtc?: string | null;
	lastMessage?: string | null;
}

interface IState {
	lastMissingRunUtc?: string | null;
	lastUpgradeRunUtc?: string | null;
	recentRuns: IRun[];
	upgradeStages: IStage[];
}

interface IStatus {
	settings: IAutomationSettings;
	state: IState;
	snapshotAvailable: boolean;
	snapshotItemLimitPerState: number;
	snapshotHasMore: boolean;
}

const settings = reactive<IAutomationSettings>({
	missing: {
		enabled: false,
		dryRun: true,
		movies: true,
		tvEpisodes: true,
		intervalMinutes: 60,
		maxItemsPerRun: 3,
	},
	upgrades: {
		enabled: false,
		mode: 'DryRun',
		movies: true,
		tvEpisodes: true,
		intervalMinutes: 180,
		maxItemsPerRun: 1,
	},
});

const state = reactive<IState>({
	lastMissingRunUtc: null,
	lastUpgradeRunUtc: null,
	recentRuns: [],
	upgradeStages: [],
});

const status = reactive<IStatus>({
	settings,
	state,
	snapshotAvailable: false,
	snapshotItemLimitPerState: 0,
	snapshotHasMore: false,
});

const loading = ref(false);
const saving = ref(false);
const runningMissing = ref(false);
const runningUpgrades = ref(false);
const finalizing = ref(false);
const message = ref('');
const messageSuccess = ref(false);

const intervalOptions = [
	{ label: '15 minutes', value: 15 },
	{ label: '30 minutes', value: 30 },
	{ label: '1 hour', value: 60 },
	{ label: '3 hours', value: 180 },
	{ label: '6 hours', value: 360 },
	{ label: '12 hours', value: 720 },
];

const upgradeIntervalOptions = [
	{ label: '30 minutes', value: 30 },
	{ label: '1 hour', value: 60 },
	{ label: '3 hours', value: 180 },
	{ label: '6 hours', value: 360 },
	{ label: '12 hours', value: 720 },
	{ label: '24 hours', value: 1440 },
];

const maxItemOptions = [1, 2, 3, 5, 10, 20].map((value) => ({
	label: value.toString(),
	value,
}));

const upgradeModeOptions = [
	{
		label: 'Dry Run — plan only',
		value: 'DryRun',
	},
	{
		label: 'Download upgrade — keep old file',
		value: 'DownloadOnly',
	},
	{
		label: 'Download → verify → wait for my approval',
		value: 'ReplaceAfterApproval',
	},
	{
		label: 'Automatic — download → verify → delete old file',
		value: 'AutomaticReplace',
	},
];

const upgradeModeTitle = computed(() => {
	switch (settings.upgrades.mode) {
		case 'DownloadOnly':
			return 'Download only';
		case 'ReplaceAfterApproval':
			return 'Protected manual replacement';
		case 'AutomaticReplace':
			return 'Automatic verified replacement';
		case 'DryRun':
		default:
			return 'Dry Run';
	}
});

const upgradeModeDescription = computed(() => {
	switch (settings.upgrades.mode) {
		case 'DownloadOnly':
			return 'Reaparr queues the higher-quality copy and never deletes the existing *arr file.';
		case 'ReplaceAfterApproval':
			return 'Reaparr downloads the upgrade and waits until owned Plex verifies it. You decide when the old *arr file is deleted.';
		case 'AutomaticReplace':
			return 'Reaparr downloads the upgrade, verifies the replacement in owned Plex at target quality, then deletes only the captured old Radarr/Sonarr file ID.';
		case 'DryRun':
		default:
			return 'No download and no deletion. Reaparr only shows you the candidates it would choose.';
	}
});

const upgradeModeIcon = computed(() =>
	settings.upgrades.mode === 'AutomaticReplace'
		? 'mdi-shield-alert-outline'
		: settings.upgrades.mode === 'ReplaceAfterApproval'
			? 'mdi-shield-check-outline'
			: settings.upgrades.mode === 'DownloadOnly'
				? 'mdi-download-outline'
				: 'mdi-flask-outline',
);

const activeStages = computed(() =>
	state.upgradeStages.filter((stage) =>
		!['DeletedOldFile', 'Cancelled'].includes(stage.status),
	),
);

const verifiedStages = computed(() =>
	state.upgradeStages.filter((stage) => stage.status === 'Verified'),
);

const recentRuns = computed(() => state.recentRuns.slice(0, 8));

function applyStatus(next: IStatus) {
	Object.assign(settings.missing, next.settings.missing);
	Object.assign(settings.upgrades, next.settings.upgrades);
	state.lastMissingRunUtc = next.state.lastMissingRunUtc ?? null;
	state.lastUpgradeRunUtc = next.state.lastUpgradeRunUtc ?? null;
	state.recentRuns = next.state.recentRuns ?? [];
	state.upgradeStages = next.state.upgradeStages ?? [];
	status.snapshotAvailable = next.snapshotAvailable;
	status.snapshotItemLimitPerState = next.snapshotItemLimitPerState ?? 0;
	status.snapshotHasMore = next.snapshotHasMore ?? false;
}

async function loadStatus() {
	loading.value = true;
	try {
		const response = await Axios.get<IStatus>('/api/Integration/MediaAutomation');
		applyStatus(response.data);
	} catch {
		messageSuccess.value = false;
		message.value = 'Media Automation status could not be loaded.';
	} finally {
		loading.value = false;
	}
}

async function save() {
	saving.value = true;
	message.value = '';

	try {
		const response = await Axios.put<IStatus>(
			'/api/Integration/MediaAutomation',
			settings,
		);
		applyStatus(response.data);
		await loadStatus();
		messageSuccess.value = true;
		message.value = 'Media Automation settings saved.';
	} catch {
		messageSuccess.value = false;
		message.value = 'Media Automation settings could not be saved.';
	} finally {
		saving.value = false;
	}
}

async function runNow(engine: 'Missing' | 'Upgrades', dryRun: boolean) {
	if (engine === 'Missing') {
		runningMissing.value = true;
	} else {
		runningUpgrades.value = true;
	}

	message.value = '';

	try {
		const response = await Axios.post<IStatus>(
			'/api/Integration/MediaAutomation/Run',
			{
				engine,
				force: true,
				dryRun,
			},
		);
		applyStatus(response.data);
		await loadStatus();
		messageSuccess.value = true;
		message.value = dryRun
			? `${engine} Dry Run completed. Review the plan below.`
			: `${engine} automation run completed.`;
	} catch {
		messageSuccess.value = false;
		message.value = `${engine} automation could not run.`;
	} finally {
		runningMissing.value = false;
		runningUpgrades.value = false;
	}
}

async function finalizeStage(stageId: string) {
	finalizing.value = true;
	try {
		const response = await Axios.post<IStatus>(
			'/api/Integration/MediaAutomation/Finalize',
			{
				stageId,
				allVerified: false,
			},
		);
		applyStatus(response.data);
		await loadStatus();
		messageSuccess.value = true;
		message.value = 'Verified old-file deletion finalized.';
	} catch {
		messageSuccess.value = false;
		message.value = 'The old file could not be finalized.';
	} finally {
		finalizing.value = false;
	}
}

async function finalizeAll() {
	finalizing.value = true;
	try {
		const response = await Axios.post<IStatus>(
			'/api/Integration/MediaAutomation/Finalize',
			{
				stageId: null,
				allVerified: true,
			},
		);
		applyStatus(response.data);
		await loadStatus();
		messageSuccess.value = true;
		message.value = 'All verified staged upgrades were finalized.';
	} catch {
		messageSuccess.value = false;
		message.value = 'Verified staged upgrades could not be finalized.';
	} finally {
		finalizing.value = false;
	}
}

async function cancelStage(stageId: string) {
	try {
		const response = await Axios.post<IStatus>(
			'/api/Integration/MediaAutomation/CancelStage',
			{ stageId },
		);
		applyStatus(response.data);
		await loadStatus();
		messageSuccess.value = true;
		message.value = 'Old-file deletion disabled for that staged upgrade.';
	} catch {
		messageSuccess.value = false;
		message.value = 'The staged deletion could not be disabled.';
	}
}

function stageColor(stageStatus: string): string {
	switch (stageStatus) {
		case 'Verified':
			return 'positive';
		case 'Failed':
			return 'negative';
		case 'WaitingForReplacement':
			return 'warning';
		default:
			return 'secondary';
	}
}

function stageIcon(stageStatus: string): string {
	switch (stageStatus) {
		case 'Verified':
			return 'mdi-shield-check-outline';
		case 'Failed':
			return 'mdi-alert-circle-outline';
		case 'WaitingForReplacement':
			return 'mdi-progress-clock';
		default:
			return 'mdi-download-outline';
	}
}

function formatDate(value: string): string {
	const date = new Date(value);
	return Number.isNaN(date.getTime())
		? value
		: date.toLocaleString();
}

onMounted(loadStatus);
</script>
