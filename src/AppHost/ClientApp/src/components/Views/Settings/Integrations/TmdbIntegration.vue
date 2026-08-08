<template>
	<QSection header="TMDB">
		<QAlert type="info">
			TMDB is optional. Reaparr already stores Plex TMDB, TVDB, IMDb and Plex GUID values; this token is used to enrich media that is missing a TMDB ID.
		</QAlert>

		<HelpRow
			:col-label="3"
			label="API Read Access Token"
			text="Create an API Read Access Token in your TMDB account settings. Reaparr stores it in your Config directory and does not return the saved token to the browser."
			title="TMDB API Read Access Token">
			<ApiKeyInputField
				v-model="readAccessToken"
				v-model:has-focus="tokenFocus"
				cy="tmdb-read-access-token"
				hint="eyJhbGciOiJIUzI1NiJ9..."
				show-strength />
		</HelpRow>

		<HelpRow
			:col-label="3"
			label="Status"
			text="TMDB enrichment is only needed when Plex does not already provide a canonical external ID."
			title="TMDB integration status">
			<div class="row items-center q-gutter-sm">
				<q-chip
					:color="status.isConfigured ? 'positive' : 'grey-7'"
					text-color="white"
					:icon="status.isConfigured ? 'mdi-check-circle' : 'mdi-circle-outline'">
					{{ status.isConfigured ? 'Configured' : 'Not configured' }}
				</q-chip>
				<q-chip
					outline
					color="secondary"
					icon="mdi-database">
					{{ status.identityCacheEntries }} cached identity lookups
				</q-chip>
			</div>
		</HelpRow>

		<HelpRow
			:col-label="3"
			hide-label
			text="Test the token before saving it."
			title="Test TMDB connection">
			<div class="row q-gutter-sm">
				<BaseButton
					:loading="testing"
					icon="mdi-connection"
					label="Test Connection"
					cy="test-tmdb-connection-button"
					@click="testConnection" />
				<BaseButton
					:loading="saving"
					:disabled="!readAccessToken.trim()"
					color="positive"
					icon="mdi-content-save"
					label="Save TMDB"
					cy="save-tmdb-button"
					@click="save" />
			</div>
		</HelpRow>

		<HelpRow
			:col-label="3"
			hide-label
			text="Clearing the identity cache forces TMDB enrichment to be resolved again the next time Discover refreshes."
			title="TMDB cache">
			<div class="row q-gutter-sm">
				<BaseButton
					:loading="clearingCache"
					color="warning"
					icon="mdi-database-refresh"
					label="Clear Identity Cache"
					cy="clear-tmdb-identity-cache-button"
					@click="clearCache" />
				<BaseButton
					:loading="clearing"
					color="negative"
					icon="mdi-trash-can-outline"
					label="Clear Configuration"
					cy="clear-tmdb-configuration-button"
					@click="clearConfiguration" />
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

interface ITmdbStatus {
	isConfigured: boolean;
	identityCacheEntries: number;
	updatedAt?: string | null;
}

interface ITmdbTestResponse {
	isSuccess: boolean;
	message: string;
}

const readAccessToken = ref('');
const tokenFocus = ref(false);
const testing = ref(false);
const saving = ref(false);
const clearing = ref(false);
const clearingCache = ref(false);
const message = ref('');
const messageSuccess = ref(false);

const status = reactive<ITmdbStatus>({
	isConfigured: false,
	identityCacheEntries: 0,
	updatedAt: null,
});

function applyStatus(next: ITmdbStatus) {
	status.isConfigured = next.isConfigured;
	status.identityCacheEntries = next.identityCacheEntries;
	status.updatedAt = next.updatedAt ?? null;
}

async function loadStatus() {
	try {
		const response = await Axios.get<ITmdbStatus>('/api/Integration/Tmdb');
		applyStatus(response.data);
	} catch {
		messageSuccess.value = false;
		message.value = 'Could not load TMDB integration status.';
	}
}

async function testConnection() {
	testing.value = true;
	message.value = '';
	try {
		const response = await Axios.post<ITmdbTestResponse>('/api/Integration/Tmdb/Test', {
			readAccessToken: readAccessToken.value,
		});
		messageSuccess.value = response.data.isSuccess;
		message.value = response.data.message;
	} catch {
		messageSuccess.value = false;
		message.value = 'TMDB connection test failed.';
	} finally {
		testing.value = false;
	}
}

async function save() {
	saving.value = true;
	message.value = '';
	try {
		const response = await Axios.put<ITmdbStatus>('/api/Integration/Tmdb', {
			readAccessToken: readAccessToken.value,
		});
		applyStatus(response.data);
		readAccessToken.value = '';
		messageSuccess.value = true;
		message.value = 'TMDB integration saved.';
	} catch {
		messageSuccess.value = false;
		message.value = 'TMDB integration could not be saved.';
	} finally {
		saving.value = false;
	}
}

async function clearConfiguration() {
	clearing.value = true;
	message.value = '';
	try {
		const response = await Axios.delete<ITmdbStatus>('/api/Integration/Tmdb');
		applyStatus(response.data);
		readAccessToken.value = '';
		messageSuccess.value = true;
		message.value = 'TMDB integration cleared.';
	} catch {
		messageSuccess.value = false;
		message.value = 'TMDB integration could not be cleared.';
	} finally {
		clearing.value = false;
	}
}

async function clearCache() {
	clearingCache.value = true;
	message.value = '';
	try {
		const response = await Axios.delete<ITmdbStatus>('/api/Integration/Tmdb/IdentityCache');
		applyStatus(response.data);
		messageSuccess.value = true;
		message.value = 'TMDB identity cache cleared.';
	} catch {
		messageSuccess.value = false;
		message.value = 'TMDB identity cache could not be cleared.';
	} finally {
		clearingCache.value = false;
	}
}

onMounted(loadStatus);
</script>
