<template>
	<QPage class="v7-page v7-settings-page">
		<ThemeOnly>
			<section class="v7-page-hero v7-page-hero--compact">
				<div>
					<div class="v7-page-kicker">
						<q-icon name="mdi-tune-vertical-variant" />
						System controls
					</div>
					<h1 class="v7-page-title">
						Advanced
					</h1>
					<p class="v7-page-subtitle">
						Diagnostics, download behavior, networking, setup controls and Reaparr cache maintenance.
					</p>
				</div>
			</section>
		</ThemeOnly>

		<section class="v7-settings-stack">
			<q-expansion-item
				v-model="debugExpanded"
				class="v77-integration-expansion"
				header-class="v77-integration-expansion__header"
				icon="mdi-bug-outline"
				:label="$t('pages.settings.advanced.debug.header')"
				caption="Diagnostic logging and debug tooling">
				<div class="v77-integration-expansion__content">
					<DebugSection />
				</div>
			</q-expansion-item>

			<q-expansion-item
				v-model="downloadManagerExpanded"
				class="v77-integration-expansion"
				header-class="v77-integration-expansion__header"
				icon="mdi-download-network-outline"
				:label="$t('pages.settings.advanced.download-manager.header')"
				caption="Download segments and completed-file handling">
				<div class="v77-integration-expansion__content">
					<DownloadManagerSection />
				</div>
			</q-expansion-item>

			<q-expansion-item
				v-model="networkExpanded"
				class="v77-integration-expansion"
				header-class="v77-integration-expansion__header"
				icon="mdi-lan"
				:label="$t('pages.settings.advanced.network.header')"
				caption="Public URL, base path and proxy headers">
				<div class="v77-integration-expansion__content">
					<NetworkSection />
				</div>
			</q-expansion-item>

			<q-expansion-item
				v-model="setupExpanded"
				class="v77-integration-expansion"
				header-class="v77-integration-expansion__header"
				icon="mdi-wrench-outline"
				:label="$t('pages.settings.advanced.setup.header')"
				caption="Re-run the Reaparr setup wizard">
				<div class="v77-integration-expansion__content">
					<SetupSection />
				</div>
			</q-expansion-item>

			<q-expansion-item
				v-model="performanceExpanded"
				class="v77-integration-expansion"
				header-class="v77-integration-expansion__header"
				icon="mdi-speedometer"
				label="Performance &amp; Cache"
				caption="Discover snapshot and poster cache maintenance">
				<div class="v77-integration-expansion__content">
					<DiscoverPerformanceIntegration />
				</div>
			</q-expansion-item>
		</section>
	</QPage>
</template>

<script setup lang="ts">
import { useLocalStorage } from '@vueuse/core';

const debugExpanded = useLocalStorage<boolean>('reaparr-settings-debug-expanded', false);
const downloadManagerExpanded = useLocalStorage<boolean>('reaparr-settings-download-manager-expanded', false);
const networkExpanded = useLocalStorage<boolean>('reaparr-settings-network-expanded', false);
const setupExpanded = useLocalStorage<boolean>('reaparr-settings-setup-expanded', false);
const performanceExpanded = useLocalStorage<boolean>('reaparr-settings-performance-expanded', false);
</script>
