<template>
	<QPage class="v7-page v7-media-page">
		<ThemeOnly>
			<section class="v7-page-hero v7-page-hero--compact">
				<div>
					<div class="v7-page-kicker">
						<q-icon name="mdi-view-dashboard-outline" />
						Unified library
					</div>
					<h1 class="v7-page-title">
						All Media
					</h1>
					<p class="v7-page-subtitle">
						A single view across the media Reaparr knows about, with the same filtering and comparison tools underneath.
					</p>
				</div>
			</section>
		</ThemeOnly>

		<section class="v7-media-overview-shell">
			<MediaOverview :library-id="0" />
		</section>
	</QPage>
</template>
