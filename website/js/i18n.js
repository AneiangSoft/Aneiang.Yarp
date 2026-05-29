/**
 * i18n - Internationalization support
 * Easy to maintain: just add/modify entries in the messages object
 */
const messages = {
  'zh-CN': {
    nav: {
      features: '功能特性',
      preview: '界面预览',
      quickstart: '快速开始',
      packages: 'NuGet 包',
    },
    hero: {
      title: '基于 YARP 的全功能 API 网关增强方案',
      subtitle: 'Dashboard 可视化管理 · 动态路由运行时配置 · 微服务一行代码自动注册 · IP 隔离负载均衡',
      getStarted: '快速开始',
      liveDemo: '在线演示',
      demoHint: '演示登录：admin / demo123',
    },
    features: {
      title: '核心功能',
      desc: '三个 NuGet 包协同工作，解决真实网关痛点',
      routing: {
        title: '动态路由管理',
        desc: '运行时 CRUD 路由与集群，无需重启。双配置源统一，自动持久化到 gateway-dynamic.json，线程安全。',
      },
      registration: {
        title: '客户端自动注册',
        desc: '一行代码 AddAneiangYarpClient()，微服务启动自动注册、关闭自动注销。智能推断所有默认值。',
      },
      ipIsolation: {
        title: 'IP 隔离负载均衡',
        desc: '多人开发调试利器。同一服务多人并行，网关按客户端 IP 自动路由，前端零感知。',
      },
      dashboard: {
        title: '可视化 Dashboard',
        desc: '两行代码启用。集群/路由 CRUD、配置导入导出、快照回滚、实时日志、日志脱敏与采样、中英文切换。',
      },
      auth: {
        title: '多模式认证鉴权',
        desc: 'None / API Key / JWT（默认 & 自定义）/ 自定义委托。智能凭据推断，少写配置。',
      },
      config: {
        title: '配置管理与回滚',
        desc: '导入导出标准 YARP 格式配置。每次变更自动快照，一键回滚到任意历史版本。',
      },
    },
    preview: {
      title: 'Dashboard 界面预览',
      desc: '两行代码即可拥有的专业管理面板',
      cluster: '集群管理',
      route: '路由管理',
      editor: 'JSON 编辑',
      logs: '请求日志',
      overview: '运行概览',
    },
    quickstart: {
      title: '快速开始',
      desc: '只需几分钟，搭建完整的 API 网关',
      copy: '复制',
      copied: '已复制',
      step1: { title: '创建网关' },
      step2: { title: '创建微服务' },
      step3: {
        title: '完成',
        desc: '微服务启动时自动注册到网关，关闭时自动注销。打开 /apigateway 访问 Dashboard 管理面板。',
      },
      arch: {
        client: '前端 / 客户端',
        services: '微服务集群',
      },
    },
    packages: {
      title: 'NuGet 包',
      desc: '按需引用，依赖关系清晰',
      core: { desc: '网关核心：动态路由、配置持久化、API 鉴权、IP 隔离负载均衡' },
      client: { desc: '客户端自动注册：一行代码接入，启动注册、关闭注销，无 YARP SDK 依赖' },
      dashboard: { desc: '可视化管理面板：集群/路由 CRUD、配置导入导出、快照回滚、实时日志' },
      dep: {
        title: '依赖关系',
        hint: '客户端服务仅需引用 Aneiang.Yarp.Client，网关服务引用 Aneiang.Yarp + Aneiang.Yarp.Dashboard',
      },
    },
    cta: {
      title: '准备好开始了吗？',
      desc: '只需几行代码，即可拥有企业级 API 网关管理能力',
      start: '立即开始',
      star: '⭐ Star on GitHub',
    },
    footer: {
      demo: '在线演示',
      built: '基于微软 YARP 2.3.0 构建',
    },
  },
  'en': {
    nav: {
      features: 'Features',
      preview: 'Preview',
      quickstart: 'Quick Start',
      packages: 'Packages',
    },
    hero: {
      title: 'Full-Featured YARP API Gateway Enhancement',
      subtitle: 'Dashboard UI · Dynamic Runtime Routing · One-Line Auto-Registration · IP Isolation Load Balancing',
      getStarted: 'Get Started',
      liveDemo: 'Live Demo',
      demoHint: 'Demo login: admin / demo123',
    },
    features: {
      title: 'Core Features',
      desc: 'Three NuGet packages working together to solve real gateway challenges',
      routing: {
        title: 'Dynamic Route Management',
        desc: 'Runtime CRUD for routes & clusters without restarts. Dual config sources unified, auto-persist to gateway-dynamic.json, thread-safe.',
      },
      registration: {
        title: 'Client Auto-Registration',
        desc: 'One line AddAneiangYarpClient() — microservices auto-register on startup, auto-unregister on shutdown. Smart defaults for everything.',
      },
      ipIsolation: {
        title: 'IP Isolation Load Balancing',
        desc: 'A must-have for team debugging. Multiple devs run the same service, gateway routes by client IP — frontend is completely unaware.',
      },
      dashboard: {
        title: 'Visual Dashboard',
        desc: 'Two lines of code to enable. Cluster/route CRUD, config import/export, snapshot rollback, real-time logs, log sanitization & sampling, i18n.',
      },
      auth: {
        title: 'Multi-Mode Auth',
        desc: 'None / API Key / JWT (default & custom) / custom delegate. Smart credential inference — less config.',
      },
      config: {
        title: 'Config Management & Rollback',
        desc: 'Import/export standard YARP format configs. Auto-snapshot before every change, one-click rollback to any version.',
      },
    },
    preview: {
      title: 'Dashboard Preview',
      desc: 'Professional management panel with just two lines of code',
      cluster: 'Clusters',
      route: 'Routes',
      editor: 'JSON Editor',
      logs: 'Request Logs',
      overview: 'Overview',
    },
    quickstart: {
      title: 'Quick Start',
      desc: 'Set up a complete API gateway in minutes',
      copy: 'Copy',
      copied: 'Copied',
      step1: { title: 'Create a Gateway' },
      step2: { title: 'Create a Microservice' },
      step3: {
        title: 'Done',
        desc: 'Microservice auto-registers on startup and auto-unregisters on shutdown. Open /apigateway to access the Dashboard.',
      },
      arch: {
        client: 'Frontend / Client',
        services: 'Microservices',
      },
    },
    packages: {
      title: 'NuGet Packages',
      desc: 'Reference what you need, clear dependency tree',
      core: { desc: 'Gateway core: dynamic routing, config persistence, API auth, IP isolation load balancing' },
      client: { desc: 'Client auto-registration: one-line integration, no YARP SDK dependency' },
      dashboard: { desc: 'Visual dashboard: cluster/route CRUD, config import/export, snapshot rollback, real-time logs' },
      dep: {
        title: 'Dependencies',
        hint: 'Client services only need Aneiang.Yarp.Client; gateway services reference Aneiang.Yarp + Aneiang.Yarp.Dashboard',
      },
    },
    cta: {
      title: 'Ready to Get Started?',
      desc: 'Enterprise-grade API gateway management with just a few lines of code',
      start: 'Get Started',
      star: '⭐ Star on GitHub',
    },
    footer: {
      demo: 'Live Demo',
      built: 'Built on Microsoft YARP 2.3.0',
    },
  },
};

/**
 * Get a nested value from an object using a dot-separated path
 */
function getNestedValue(obj, path) {
  return path.split('.').reduce((acc, key) => acc?.[key], obj);
}

/**
 * Apply translations to all elements with data-i18n attribute
 */
function applyTranslations(lang) {
  const msg = messages[lang];
  if (!msg) return;

  document.querySelectorAll('[data-i18n]').forEach(el => {
    const key = el.getAttribute('data-i18n');
    const value = getNestedValue(msg, key);
    if (value) {
      el.textContent = value;
    }
  });

  document.documentElement.lang = lang;
  localStorage.setItem('locale', lang);
}

/**
 * Get current locale
 */
function getCurrentLocale() {
  return localStorage.getItem('locale') || 'zh-CN';
}

/**
 * Toggle locale
 */
function toggleLocale() {
  const current = getCurrentLocale();
  const next = current === 'zh-CN' ? 'en' : 'zh-CN';
  applyTranslations(next);
  const btn = document.getElementById('langToggle');
  btn.textContent = next === 'zh-CN' ? 'EN' : '中文';
}

// Export for use in main.js
window.i18n = { messages, applyTranslations, getCurrentLocale, toggleLocale };
