<template>
	<QPage class="v7-page v7-settings-page">
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

		<section class="v7-settings-stack">
			<DebugSection />
			<DownloadManagerSection />
			<NetworkSection />
			<SetupSection />
			<DiscoverPerformanceIntegration />
		</section>
	</QPage>
</template>
