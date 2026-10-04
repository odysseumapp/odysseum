var { BaseTransition: e, BaseTransitionPropsValidators: t, Comment: n, DeprecationTypes: r, EffectScope: i, ErrorCodes: a, ErrorTypeStrings: o, Fragment: s, KeepAlive: c, ReactiveEffect: ee, Static: te, Suspense: ne, Teleport: re, Text: ie, TrackOpTypes: l, Transition: ae, TransitionGroup: oe, TriggerOpTypes: se, VueElement: ce, __esModule: le, assertNumber: ue, callWithAsyncErrorHandling: de, callWithErrorHandling: fe, camelize: pe, capitalize: me, cloneVNode: he, compatUtils: ge, compile: _e, computed: ve, createApp: ye, createBlock: u, createCommentVNode: d, createElementBlock: f, createElementVNode: p, createHydrationRenderer: be, createPropsRestProxy: xe, createRenderer: Se, createSSRApp: Ce, createSlots: we, createStaticVNode: Te, createTextVNode: Ee, createVNode: m, customRef: h, defineAsyncComponent: g, defineComponent: _, defineCustomElement: v, defineEmits: y, defineExpose: b, defineModel: x, defineOptions: S, defineProps: C, defineSSRCustomElement: w, defineSlots: T, devtools: E, effect: D, effectScope: De, getCurrentInstance: Oe, getCurrentScope: ke, getCurrentWatcher: Ae, getTransitionRawChildren: je, guardReactiveProps: Me, h: Ne, handleError: Pe, hasInjectionContext: Fe, hydrate: Ie, hydrateOnIdle: Le, hydrateOnInteraction: Re, hydrateOnMediaQuery: ze, hydrateOnVisible: Be, initCustomFormatter: Ve, initDirectivesForSSR: He, inject: Ue, isMemoSame: We, isProxy: Ge, isReactive: Ke, isReadonly: qe, isRef: Je, isRuntimeOnly: Ye, isShallow: Xe, isVNode: Ze, markRaw: O, mergeDefaults: Qe, mergeModels: $e, mergeProps: et, nextTick: tt, nodeOps: nt, normalizeClass: k, normalizeProps: rt, normalizeStyle: it, onActivated: at, onBeforeMount: ot, onBeforeUnmount: st, onBeforeUpdate: ct, onDeactivated: lt, onErrorCaptured: ut, onMounted: dt, onRenderTracked: ft, onRenderTriggered: pt, onScopeDispose: mt, onServerPrefetch: ht, onUnmounted: gt, onUpdated: _t, onWatcherCleanup: vt, openBlock: A, patchProp: yt, popScopeId: bt, provide: xt, proxyRefs: St, pushScopeId: Ct, queuePostFlushCb: wt, reactive: Tt, readonly: Et, ref: Dt, registerRuntimeCompiler: Ot, render: kt, renderList: j, renderSlot: At, resolveComponent: jt, resolveDirective: Mt, resolveDynamicComponent: Nt, resolveFilter: Pt, resolveTransitionHooks: Ft, setBlockTracking: It, setDevtoolsHook: Lt, setTransitionHooks: Rt, shallowReactive: zt, shallowReadonly: Bt, shallowRef: Vt, ssrContextKey: Ht, ssrUtils: Ut, stop: Wt, toDisplayString: M, toHandlerKey: Gt, toHandlers: Kt, toRaw: qt, toRef: Jt, toRefs: Yt, toValue: Xt, transformVNodeArgs: Zt, triggerRef: Qt, unref: N, useAttrs: $t, useCssModule: en, useCssVars: tn, useHost: nn, useId: rn, useModel: an, useSSRContext: on, useShadowRoot: sn, useSlots: cn, useTemplateRef: ln, useTransitionState: un, vModelCheckbox: dn, vModelDynamic: fn, vModelRadio: pn, vModelSelect: mn, vModelText: hn, vShow: gn, version: _n, warn: vn, watch: yn, watchEffect: bn, watchPostEffect: xn, watchSyncEffect: Sn, withAsyncContext: Cn, withCtx: P, withDefaults: wn, withDirectives: Tn, withKeys: En, withMemo: F, withModifiers: I, withScopeId: Dn } = globalThis.__odysseum.vue, L = {
	viewBox: "0 0 24 24",
	width: "1.2em",
	height: "1.2em"
};
function R(e, t) {
	return A(), f("svg", L, [...t[0] ||= [p("path", {
		fill: "none",
		stroke: "currentColor",
		"stroke-linecap": "round",
		"stroke-linejoin": "round",
		"stroke-width": "2",
		d: "M12 5v14m7-7l-7 7l-7-7"
	}, null, -1)]]);
}
var z = O({
	name: "lucide-arrow-down",
	render: R
}), B = {
	viewBox: "0 0 24 24",
	width: "1.2em",
	height: "1.2em"
};
function V(e, t) {
	return A(), f("svg", B, [...t[0] ||= [p("path", {
		fill: "none",
		stroke: "currentColor",
		"stroke-linecap": "round",
		"stroke-linejoin": "round",
		"stroke-width": "2",
		d: "m5 12l7-7l7 7m-7 7V5"
	}, null, -1)]]);
}
var H = O({
	name: "lucide-arrow-up",
	render: V
}), U = "application/x-odysseum-view-item";
function W(e, t, n) {
	e.dataTransfer?.setData(U, JSON.stringify({
		folderId: t,
		id: n
	})), e.dataTransfer && (e.dataTransfer.effectAllowed = "move");
}
function G(e, t) {
	try {
		let n = JSON.parse(e.dataTransfer?.getData(U) ?? "");
		if (n.folderId === t && typeof n.id == "string") return n.id;
	} catch {}
}
//#endregion
//#region src/host.ts
function K() {
	let e = globalThis.__odysseum;
	if (!e) throw Error("The Odysseum host API is missing.");
	return e.ui;
}
//#endregion
//#region src/views/CollectionView.vue?vue&type=script&setup=true&lang.ts
var q = ["aria-label"], J = [
	"data-item-id",
	"onDragstart",
	"onDrop"
], Y = { class: "vw:flex vw:items-center vw:gap-1 vw:mt-2" }, X = {
	key: 0,
	class: "vw:text-xs vw:text-muted vw:ml-auto"
}, Z = {
	key: 0,
	class: "vw:text-muted vw:py-8"
}, Q = /* @__PURE__ */ _({
	__name: "CollectionView",
	props: {
		folder: {},
		items: {},
		compact: { type: Boolean }
	},
	emits: ["open", "move"],
	setup(e, { emit: t }) {
		let n = e, r = t, { Button: i, ItemCard: a } = K();
		function o(e, t) {
			let i = n.folder.childIds.indexOf(t);
			e !== t && i >= 0 && r("move", e, n.folder.id, i);
		}
		function c(e, t) {
			let r = G(e, n.folder.id);
			r && o(r, t);
		}
		return (t, n) => (A(), f("div", {
			class: k(e.compact ? "vw:space-y-2" : "vw:grid vw:gap-4 vw:sm:grid-cols-2 vw:xl:grid-cols-3 vw:2xl:grid-cols-4"),
			"aria-label": e.compact ? "Outline" : "Corkboard"
		}, [(A(!0), f(s, null, j(e.items, (t, s) => (A(), f("div", {
			key: t.id,
			"data-item-id": t.id,
			draggable: "true",
			class: "vw:cursor-grab",
			onDragstart: (n) => N(W)(n, e.folder.id, t.id),
			onDragover: n[0] ||= I(() => {}, ["prevent"]),
			onDrop: I((e) => c(e, t.id), ["prevent"])
		}, [m(N(a), {
			item: t,
			compact: e.compact,
			onOpen: (e) => r("open", t.id)
		}, {
			default: P(() => [p("div", Y, [
				m(N(i), {
					size: "xs",
					color: "neutral",
					variant: "ghost",
					icon: N(H),
					disabled: s === 0,
					"aria-label": `Move ${t.title} earlier`,
					onClick: (n) => o(t.id, e.items[s - 1].id)
				}, null, 8, [
					"icon",
					"disabled",
					"aria-label",
					"onClick"
				]),
				m(N(i), {
					size: "xs",
					color: "neutral",
					variant: "ghost",
					icon: N(z),
					disabled: s === e.items.length - 1,
					"aria-label": `Move ${t.title} later`,
					onClick: (n) => o(t.id, e.items[s + 1].id)
				}, null, 8, [
					"icon",
					"disabled",
					"aria-label",
					"onClick"
				]),
				t.document ? (A(), f("span", X, M(t.document.status), 1)) : d("", !0)
			])]),
			_: 2
		}, 1032, [
			"item",
			"compact",
			"onOpen"
		])], 40, J))), 128)), e.items.length ? d("", !0) : (A(), f("p", Z, "This folder is empty. Add a document or subfolder."))], 10, q));
	}
}), $ = /* @__PURE__ */ _({
	__name: "BoardView",
	props: {
		folder: {},
		items: {},
		settings: {},
		project: {}
	},
	emits: [
		"open",
		"move",
		"saveSettings",
		"updateDetails",
		"link",
		"unlink",
		"setLinkNote",
		"createDocument"
	],
	setup(e, { emit: t }) {
		let n = t;
		return (t, r) => (A(), u(Q, {
			folder: e.folder,
			items: e.items,
			compact: !1,
			onOpen: r[0] ||= (e) => n("open", e),
			onMove: r[1] ||= (e, t, r) => n("move", e, t, r)
		}, null, 8, ["folder", "items"]));
	}
});
//#endregion
export { $ as default };
