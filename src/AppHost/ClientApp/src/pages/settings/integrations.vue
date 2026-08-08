<template>
	<QPage class="v7-page v7-settings-page">
		<section class="v7-page-hero v7-page-hero--compact v7-page-hero--integrations">
			<div>
				<div class="v7-page-kicker">
					<q-icon name="mdi-connection" />
					Connected ecosystem
				</div>
				<h1 class="v7-page-title">
					Integrations
				</h1>
				<p class="v7-page-subtitle">
					Connect Plex, Radarr, Sonarr and TMDB, tune caching, reconcile completed downloads, and control the new automation engines.
				</p>
			</div>

			<div class="v7-page-hero__badge">
				<q-icon name="mdi-shield-check-outline" />
				Safe automation controls
			</div>
		</section>

		<section class="v7-settings-stack">
			<SonarrIntegration />
			<RadarrIntegration />
			<TmdbIntegration />
			<LibraryReconciliationIntegration />
			<MediaAutomationIntegration />
			<DiscoverPerformanceIntegration />
		</section>
	</QPage>
</template>
