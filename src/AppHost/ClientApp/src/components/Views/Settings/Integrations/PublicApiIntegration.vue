<template>
	<QSection>
		<QAlert type="info">
			{{ $t('components.public-api-integration.description') }}
		</QAlert>

		<HelpRow
			:col-label="3"
			label="API Key"
			text="Sent as the apikey query parameter by indexer and music-search clients. Reaparr generates this on first start; regenerating it will break any client still using the old value."
			title="Reaparr API key">
			<div class="row items-center no-wrap q-gutter-sm">
				<q-input
					:model-value="reaparrApiKey"
					class="col"
					dense
					outlined
					readonly
					data-cy="reaparr-api-key" />
				<q-btn
					flat
					dense
					icon="mdi-content-copy"
					:aria-label="$t('components.public-api-integration.copy')"
					@click="copy(reaparrApiKey)" />
			</div>
		</HelpRow>

		<HelpRow
			:col-label="3"
			label="Download client username"
			text="Used with the password below to obtain a session cookie from the qBittorrent-compatible download client API."
			title="Download client username">
			<div class="row items-center no-wrap q-gutter-sm">
				<q-input
					:model-value="downloadClientUsername"
					class="col"
					dense
					outlined
					readonly
					data-cy="download-client-username" />
				<q-btn
					flat
					dense
					icon="mdi-content-copy"
					:aria-label="$t('components.public-api-integration.copy')"
					@click="copy(downloadClientUsername)" />
			</div>
		</HelpRow>

		<HelpRow
			:col-label="3"
			label="Download client password"
			text="Paired with the username above. Both are required by clients that transfer files, such as SoulSync."
			title="Download client password">
			<div class="row items-center no-wrap q-gutter-sm">
				<q-input
					:model-value="downloadClientPassword"
					class="col"
					dense
					outlined
					readonly
					:type="revealPassword ? 'text' : 'password'"
					data-cy="download-client-password" />
				<q-btn
					flat
					dense
					:icon="revealPassword ? 'mdi-eye-off' : 'mdi-eye'"
					:aria-label="$t('components.public-api-integration.reveal')"
					@click="revealPassword = !revealPassword" />
				<q-btn
					flat
					dense
					icon="mdi-content-copy"
					:aria-label="$t('components.public-api-integration.copy')"
					@click="copy(downloadClientPassword)" />
			</div>
		</HelpRow>
	</QSection>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import { useSettingsStore } from '@store';

const settingsStore = useSettingsStore();

const revealPassword = ref(false);

const reaparrApiKey = computed(() => settingsStore.integrationsSettings.reaparrApiKey);
const downloadClientUsername = computed(() => settingsStore.integrationsSettings.downloadClientUsername);
const downloadClientPassword = computed(() => settingsStore.integrationsSettings.downloadClientPassword);

function copy(value: string): void {
	if (value) {
		navigator.clipboard?.writeText(value);
	}
}
</script>
