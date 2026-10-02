<template>
	<QSection :header="t('components.lidarr-integration.title')">
		<QAlert
			:type="settingsStore.networkSettings.reverseProxyUrl ? 'info' : 'warning'"
			to="/settings/advanced#reverse-proxy-settings">
			{{ settingsStore.networkSettings.reverseProxyUrl
				? t('components.lidarr-integration.callback-url-configured', { url: settingsStore.networkSettings.reverseProxyUrl })
				: t('components.lidarr-integration.callback-url-warning') }}
		</QAlert>
		<q-stepper
			ref="stepper"
			v-model="integrationStore.lidarr.step"
			alternative-labels
			animated
			color="primary"
			flat
			header-nav>
			<!-- Setup Lidarr Connection -->
			<QStep
				:done="integrationStore.lidarr.testSuccess === true"
				:error="integrationStore.lidarr.testSuccess === false && integrationStore.lidarr.testStatus !== null"
				:name="1"
				:title="t('components.lidarr-integration.nav-bar.connection.title')"
				active-icon="mdi-connection"
				done-color="positive"
				done-icon="mdi-check">
				<!-- Base URL -->
				<HelpRow
					:col-label="3"
					:label="t('help.settings.integrations.lidarr.base-url-input.label')"
					:text="t('help.settings.integrations.lidarr.base-url-input.text')"
					:title="t('help.settings.integrations.lidarr.base-url-input.title')">
					<QInput
						v-model="settingsStore.integrationsSettings.lidarr.lidarrBaseUrl"
						hint="http://localhost:8686"
						data-cy="lidarr-base-url-input" />
				</HelpRow>
				<!-- API Key -->
				<HelpRow
					:col-label="3"
					:label="t('help.settings.integrations.lidarr.api-key-input.label')"
					:text="t('help.settings.integrations.lidarr.api-key-input.text')"
					:title="t('help.settings.integrations.lidarr.api-key-input.title')">
					<ApiKeyInputField
						v-model="settingsStore.integrationsSettings.lidarr.lidarrApiKey"
						v-model:has-focus="passwordInputFocus"
						cy="lidarr-api-key-input"
						hint="a02a22a436504e15b7e46764b12825db"
						show-strength />
				</HelpRow>

				<!-- Test Connection -->
				<HelpRow
					:col-label="3"
					:text="t('help.settings.integrations.lidarr.test-connection.text')"
					:title="t('help.settings.integrations.lidarr.test-connection.title')"
					hide-label>
					<BaseButton
						:loading="integrationStore.lidarr.isTesting"
						:disabled="!integrationStore.isLidarrConnectionValid"
						icon="mdi-connection"
						label="Test Connection"
						cy="test-lidarr-connection-button"
						@click="testLidarrConnection" />
				</HelpRow>
				<!-- Clear Configuration -->
				<HelpRow
					:col-label="3"
					:text="t('help.settings.integrations.lidarr.clear-configuration.text')"
					:title="t('help.settings.integrations.lidarr.clear-configuration.title')"
					hide-label>
					<BaseButton
						color="negative"
						icon="mdi-trash-can-outline"
						label="Clear Configuration"
						cy="clear-lidarr-configuration-button"
						@click="clearLidarrConfiguration" />
				</HelpRow>
			</QStep>
			<!-- Configure Lidarr Integration -->
			<QStep
				:disable="integrationStore.lidarr.testSuccess === false"
				:done="settingsStore.integrationsSettings.lidarr.isConfigured"
				:name="2"
				:title="t('components.lidarr-integration.nav-bar.configure.title')"
				active-icon="mdi-cog"
				done-color="positive"
				done-icon="mdi-check"
				icon="mdi-cog">
				<!-- Setup Lidarr Integration -->
				<HelpRow
					:col-label="3"
					:text="t('help.settings.integrations.lidarr.setup-configuration.text')"
					:title="t('help.settings.integrations.lidarr.setup-configuration.title')"
					hide-label>
					<BaseButton
						:loading="integrationStore.lidarr.isConfiguring"
						icon="mdi-connection"
						:label="t('components.lidarr-integration.nav-bar.configure.button')"
						cy="configure-lidarr-button"
						@click="configureLidarrSetup" />
				</HelpRow>
			</QStep>
		</q-stepper>
		<!-- Test Connection Status -->
		<QRow>
			<QCol>
				<QAlert
					v-if="integrationStore.lidarr.configuringSuccess !== null"
					cy="test-lidarr-configuration-status-alert"
					:type="integrationStore.lidarr.configuringSuccess ? NotificationLevel.Success : NotificationLevel.Error">
					{{ integrationStore.lidarr.configuringSuccess ? t('components.lidarr-integration.configuration-status.success') : formatErrorResponse(integrationStore.lidarr.error) }}
				</QAlert>
				<!-- Always returns 200 -->
				<QAlert
					v-else-if="integrationStore.lidarr.testSuccess !== null"
					:type="integrationStore.lidarr.testSuccess ? NotificationLevel.Success : NotificationLevel.Error"
					cy="test-lidarr-connection-status-alert">
					{{ getTestStatusMessage() }}
				</QAlert>
			</QCol>
		</QRow>
	</QSection>
</template>

<script lang="ts" setup>
import { ref } from 'vue';
import { useSettingsStore, useIntegrationStore } from '@store';
import { NotificationLevel, TestConnectionStatus } from '@dto';
import { formatErrorResponse } from '@composables';

const settingsStore = useSettingsStore();
const integrationStore = useIntegrationStore();
const { t } = useI18n();

const passwordInputFocus = ref(false);

function testLidarrConnection() {
	useSubscription(integrationStore.testConnectionToLidarr().subscribe());
}

function clearLidarrConfiguration() {
	useSubscription(integrationStore.clearLidarrConfiguration().subscribe());
}

function configureLidarrSetup() {
	useSubscription(integrationStore.configureLidarrIntegration().subscribe());
}

function getTestStatusMessage(): string {
	const status = integrationStore.lidarr.testStatus;

	if (!status) {
		return integrationStore.lidarr.error
			? formatErrorResponse(integrationStore.lidarr.error)
			: '';
	}

	switch (status) {
		case TestConnectionStatus.Success:
			return t('components.lidarr-integration.connection-status.success');
		case TestConnectionStatus.InvalidApiKey:
			return t('components.lidarr-integration.connection-status.invalid-api-key');
		case TestConnectionStatus.ConnectionFailed:
			return t('components.lidarr-integration.connection-status.connection-failed');
		case TestConnectionStatus.UrlIsInvalid:
			return t('components.lidarr-integration.connection-status.url-is-invalid');
		case TestConnectionStatus.Unknown:
		default:
			return t('components.lidarr-integration.connection-status.unknown-connection-status');
	}
}
</script>
