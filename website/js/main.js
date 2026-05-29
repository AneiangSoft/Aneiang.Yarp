/**
 * Main JS - Interactions, scroll animations, copy buttons
 */
(function () {
  'use strict';

  // ===== i18n Init =====
  const locale = window.i18n.getCurrentLocale();
  window.i18n.applyTranslations(locale);
  const langBtn = document.getElementById('langToggle');
  langBtn.textContent = locale === 'zh-CN' ? 'EN' : '中文';
  langBtn.addEventListener('click', window.i18n.toggleLocale);

  // ===== Mobile Menu =====
  const mobileBtn = document.getElementById('mobileMenuBtn');
  const navLinks = document.getElementById('navLinks');
  mobileBtn.addEventListener('click', () => {
    navLinks.classList.toggle('open');
    mobileBtn.classList.toggle('active');
  });
  // Close on link click
  navLinks.querySelectorAll('a').forEach(a => {
    a.addEventListener('click', () => {
      navLinks.classList.remove('open');
      mobileBtn.classList.remove('active');
    });
  });

  // ===== Navbar Scroll Effect =====
  const navbar = document.getElementById('navbar');
  window.addEventListener('scroll', () => {
    navbar.classList.toggle('scrolled', window.scrollY > 50);
  }, { passive: true });

  // ===== Preview Tabs =====
  const tabs = document.querySelectorAll('.preview-tab');
  const items = document.querySelectorAll('.preview-item');
  tabs.forEach(tab => {
    tab.addEventListener('click', () => {
      tabs.forEach(t => t.classList.remove('active'));
      items.forEach(i => i.classList.remove('active'));
      tab.classList.add('active');
      document.getElementById('preview-' + tab.dataset.tab).classList.add('active');
    });
  });

  // ===== Copy Buttons =====
  document.querySelectorAll('.copy-btn').forEach(btn => {
    btn.addEventListener('click', () => {
      const code = btn.closest('.code-block').querySelector('code').textContent;
      navigator.clipboard.writeText(code).then(() => {
        const orig = btn.textContent;
        btn.textContent = window.i18n.getCurrentLocale() === 'zh-CN' ? '已复制' : 'Copied';
        btn.classList.add('copied');
        setTimeout(() => {
          btn.textContent = orig;
          btn.classList.remove('copied');
        }, 1500);
      });
    });
  });

  // ===== Scroll Reveal =====
  const observer = new IntersectionObserver(
    (entries) => {
      entries.forEach(entry => {
        if (entry.isIntersecting) {
          const delay = entry.target.dataset.delay || 0;
          setTimeout(() => entry.target.classList.add('visible'), delay);
          observer.unobserve(entry.target);
        }
      });
    },
    { threshold: 0.1, rootMargin: '0px 0px -40px 0px' }
  );

  document.querySelectorAll('.feature-card').forEach(card => observer.observe(card));

  // ===== Smooth anchor links =====
  document.querySelectorAll('a[href^="#"]').forEach(a => {
    a.addEventListener('click', (e) => {
      const target = document.querySelector(a.getAttribute('href'));
      if (target) {
        e.preventDefault();
        target.scrollIntoView({ behavior: 'smooth' });
      }
    });
  });
})();
