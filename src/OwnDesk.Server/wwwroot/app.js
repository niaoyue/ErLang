(() => {
  if (window.__ownDeskAppLoading) {
    return;
  }

  window.__ownDeskAppLoading = true;

  const version = "20260428-webrtc-ice3";
  const scriptNames = [
    "app.state.js",
    "app.organizations.js",
    "app.auth.js",
    "app.webrtc.helpers.js",
    "app.webrtc.js",
    "app.rendering.js",
    "app.pointer.js",
    "app.controls.js",
    "app.bootstrap.js"
  ];
  const loaderScript = document.currentScript;
  const baseUrl = new URL(".", loaderScript?.src || window.location.href);

  let chain = Promise.resolve();
  for (const scriptName of scriptNames) {
    chain = chain.then(() => loadScript(new URL(`${scriptName}?v=${version}`, baseUrl).toString()));
  }

  chain.catch((error) => {
    console.error("OwnDesk console failed to load.", error);
  });

  function loadScript(src) {
    return new Promise((resolve, reject) => {
      const script = document.createElement("script");
      script.src = src;
      script.async = false;
      script.onload = resolve;
      script.onerror = () => reject(new Error(`Failed to load ${src}`));
      document.head.appendChild(script);
    });
  }
})();
