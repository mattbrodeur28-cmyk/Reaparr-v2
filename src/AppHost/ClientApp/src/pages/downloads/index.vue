<template>
	<QPage class="v7-page v75-downloads-page">
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
			class="v7-page-content v75-downloads-content">
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

			<div class="v7-download-groups v75-download-groups">
				<div
					v-for="{ plexServer, downloads } in downloadStore.getServersWithDownloads"
					:key="plexServer.id"
					class="v7-download-group v75-download-group">
					<DownloadsTable
						:download-rows="downloads"
						:plex-server="plexServer" />
				</div>
			</div>

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

<style lang="scss">
.v75-downloads-page,
.v75-downloads-content {
  min-height: 0;
}

.v75-download-groups {
  display: grid;
  min-height: 1px;
  gap: 14px;
  padding-bottom: max(24px, env(safe-area-inset-bottom));
}

.v75-download-group {
  min-width: 0;
  overflow: visible;
}

/*
 * V7 used QScroll here. Without an explicit calculated height Safari could
 * collapse the scroll viewport to 0px even while the store contained rows.
 * V7.5 deliberately uses the page's native scroll instead.
 */
.v75-download-group .q-table__container {
  width: 100%;
  min-height: 1px;
}

@media (max-width: 760px) {
  .v75-downloads-content {
    margin-top: 12px;
  }

  .v75-download-groups {
    gap: 10px;
  }
}
</style>
