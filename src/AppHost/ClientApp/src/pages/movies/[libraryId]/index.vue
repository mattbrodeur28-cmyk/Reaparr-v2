<template>
	<QPage class="v7-page v7-media-page">
		<section class="v7-page-hero v7-page-hero--compact">
			<div>
				<div class="v7-page-kicker">
					<q-icon name="mdi-movie-open-outline" />
					Movie library
				</div>
				<h1 class="v7-page-title">
					Movies
				</h1>
				<p class="v7-page-subtitle">
					Browse, compare and manage this Plex movie library with the faster Reaparr media workspace.
				</p>
			</div>
		</section>

		<section class="v7-media-overview-shell">
			<MediaOverview :library-id="libraryId" />
		</section>
	</QPage>
</template>

<script setup lang="ts">
import { useRoute } from 'vue-router';
import { definePageMeta } from '#imports';

definePageMeta({
	scrollToTop: false,
});

const route = useRoute();
const libraryId = +(route.params.libraryId as string);
</script>
