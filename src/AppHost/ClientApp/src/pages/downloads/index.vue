<template>
	<QPage class="v7-page">
		<section class="v7-page-hero v7-page-hero--downloads">
			<div>
				<div class="v7-page-kicker">
					<q-icon name="mdi-download-circle-outline" />
					Transfer center
				</div>
				<h1 class="v7-page-title">
					Downloads
				</h1>
				<p class="v7-page-subtitle">
					Follow active transfers, completed media, source servers and post-download processing from one clean workspace.
				</p>
			</div>

			<div class="v7-page-metrics">
				<div class="v7-page-metric">
					<span>Active</span>
					<strong>{{ downloadStore.getActiveDownloadList().length }}</strong>
				</div>
				<div class="v7-page-metric">
					<span>Servers</span>
					<strong>{{ downloadStore.getServersWithDownloads.length }}</strong>
				</div>
			</div>
		</section>

		<section
			v-if="downloadStore.getServersWithDownloads.length > 0"
			class="v7-page-content">
			<div class="v7-section-heading">
				<div>
					<div class="v7-section-eyebrow">
						Live queue
					</div>
					<div class="v7-section-title">
						Transfers by Plex server
					</div>
				</div>
				<div class="v7-live-pill">
					<span class="v7-live-dot" />
					Live
				</div>
			</div>

			<QScroll class="page-content-minus-download-bar v7-download-scroll">
				<div class="v7-download-groups">
					<div
						v-for="{ plexServer, downloads } in downloadStore.getServersWithDownloads"
						:key="plexServer.id"
						class="v7-download-group">
						<DownloadsTable
							:download-rows="downloads"
							:plex-server="plexServer" />
					</div>
				</div>
			</QScroll>

			<DownloadDetailsDialog />
		</section>

		<section
			v-else
			class="v7-empty-state">
			<div class="v7-empty-state__icon">
				<q-icon name="mdi-tray-check" />
			</div>
			<div class="v7-empty-state__title">
				Queue is clear
			</div>
			<div class="v7-empty-state__copy">
				{{ $t('pages.downloads.no-downloads') }}
			</div>
			<q-btn
				to="/discover"
				unelevated
				no-caps
				rounded
				icon="mdi-compass-rose"
				label="Open Discover"
				class="v5-primary-action q-mt-lg" />
		</section>
	</QPage>
</template>

<script setup lang="ts">
import { useDownloadStore } from '@store';

const downloadStore = useDownloadStore();
</script>
