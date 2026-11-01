/* SIMS — app shell behaviour: theme, sidebar, user menu, command palette. */
(function () {
  'use strict';

  var root = document.documentElement;

  /* ── Theme ─────────────────────────────────────────────────────────────── */

  function currentTheme() {
    return root.getAttribute('data-theme') === 'dark' ? 'dark' : 'light';
  }

  function applyTheme(theme) {
    root.setAttribute('data-theme', theme);
    root.setAttribute('data-bs-theme', theme);
    try {
      localStorage.setItem('sims-theme', theme);
    } catch (e) { /* chế độ riêng tư */ }

    var label = document.getElementById('theme-label');
    if (label) {
      label.textContent = theme === 'dark' ? 'Giao diện tối' : 'Giao diện sáng';
    }
    var toggle = document.getElementById('theme-toggle');
    if (toggle) {
      toggle.setAttribute('aria-label', theme === 'dark' ? 'Chuyển sang giao diện sáng' : 'Chuyển sang giao diện tối');
    }
  }

  function toggleTheme() {
    applyTheme(currentTheme() === 'dark' ? 'light' : 'dark');
  }

  applyTheme(currentTheme());

  ['theme-toggle', 'theme-toggle-top', 'theme-toggle-menu'].forEach(function (id) {
    var el = document.getElementById(id);
    if (el) {
      el.addEventListener('click', toggleTheme);
    }
  });

  /* ── Sidebar (mobile) ──────────────────────────────────────────────────── */

  var shell = document.getElementById('app-shell');
  var sidebarToggle = document.getElementById('sidebar-toggle');
  var scrim = document.getElementById('sidebar-scrim');

  function setSidebar(open) {
    if (!shell) {
      return;
    }
    shell.classList.toggle('sidebar-open', open);
    if (sidebarToggle) {
      sidebarToggle.setAttribute('aria-expanded', open ? 'true' : 'false');
    }
    if (open) {
      var first = document.querySelector('#app-sidebar .nav-link');
      if (first) {
        first.focus({ preventScroll: true });
      }
    }
  }

  if (sidebarToggle) {
    sidebarToggle.addEventListener('click', function () {
      setSidebar(!shell.classList.contains('sidebar-open'));
    });
  }
  if (scrim) {
    scrim.addEventListener('click', function () { setSidebar(false); });
  }
  document.addEventListener('keydown', function (event) {
    if (event.key === 'Escape' && shell && shell.classList.contains('sidebar-open')) {
      setSidebar(false);
      if (sidebarToggle) { sidebarToggle.focus(); }
    }
  });

  /* ── User menu ─────────────────────────────────────────────────────────── */

  var userTrigger = document.getElementById('user-menu-trigger');
  var userMenu = document.getElementById('user-menu');

  function closeUserMenu() {
    if (userMenu && !userMenu.hidden) {
      userMenu.hidden = true;
      if (userTrigger) { userTrigger.setAttribute('aria-expanded', 'false'); }
    }
  }

  if (userTrigger && userMenu) {
    userTrigger.addEventListener('click', function (event) {
      event.stopPropagation();
      var open = userMenu.hidden;
      userMenu.hidden = !open;
      userTrigger.setAttribute('aria-expanded', open ? 'true' : 'false');
      if (open) {
        var firstItem = userMenu.querySelector('.menu-item');
        if (firstItem) { firstItem.focus({ preventScroll: true }); }
      }
    });
    document.addEventListener('click', function (event) {
      if (!userMenu.hidden && !userMenu.contains(event.target) && event.target !== userTrigger) {
        closeUserMenu();
      }
    });
    document.addEventListener('keydown', function (event) {
      if (event.key === 'Escape') { closeUserMenu(); }
    });
    userMenu.addEventListener('keydown', function (event) {
      if (event.key !== 'Tab') { return; }
      var items = Array.prototype.slice.call(
        userMenu.querySelectorAll('a[href], button, [tabindex]:not([tabindex="-1"])'));
      if (!items.length) { return; }
      var first = items[0];
      var last = items[items.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault(); last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault(); first.focus();
      }
    });
  }

  var menuTheme = document.getElementById('theme-toggle-menu');
  if (menuTheme) {
    menuTheme.addEventListener('click', closeUserMenu);
  }

  /* ── Command palette ───────────────────────────────────────────────────── */

  var cmdk = document.getElementById('cmdk');
  var cmdkInput = document.getElementById('cmdk-input');
  var cmdkList = document.getElementById('cmdk-list');
  var searchTrigger = document.getElementById('search-trigger');
  var entries = [];
  var filtered = [];
  var selected = 0;

  if (cmdk && cmdkList) {
    var links = document.querySelectorAll('#app-sidebar .app-nav a.nav-link');
    Array.prototype.forEach.call(links, function (link) {
      var text = link.querySelector('.nav-link__text');
      var label = text ? text.textContent.trim() : link.textContent.trim();
      var icon = link.querySelector('.nav-link__icon');
      entries.push({
        label: label,
        href: link.getAttribute('href'),
        icon: icon ? icon.innerHTML : ''
      });
    });
  }

  function renderCmdk() {
    if (!cmdkList) { return; }
    cmdkList.innerHTML = '';
    if (!filtered.length) {
      var empty = document.createElement('li');
      empty.className = 'cmdk__empty';
      empty.textContent = 'Không tìm thấy trang phù hợp.';
      cmdkList.appendChild(empty);
      cmdkInput.removeAttribute('aria-activedescendant');
      return;
    }
    filtered.forEach(function (entry, index) {
      var li = document.createElement('li');
      li.className = 'cmdk__item';
      li.id = 'cmdk-item-' + index;
      li.setAttribute('role', 'option');
      li.setAttribute('aria-selected', index === selected ? 'true' : 'false');
      li.innerHTML = '<span class="nav-link__icon" aria-hidden="true"></span><span></span>';
      li.firstChild.innerHTML = entry.icon;
      li.lastChild.textContent = entry.label;
      li.addEventListener('mouseenter', function () {
        selected = index;
        updateSelection();
      });
      li.addEventListener('click', function () {
        window.location.href = entry.href;
      });
      cmdkList.appendChild(li);
    });
    updateSelection();
  }

  function updateSelection() {
    if (!cmdkList || !cmdkInput) { return; }
    var items = cmdkList.querySelectorAll('.cmdk__item');
    Array.prototype.forEach.call(items, function (item, index) {
      item.setAttribute('aria-selected', index === selected ? 'true' : 'false');
    });
    if (items[selected]) {
      cmdkInput.setAttribute('aria-activedescendant', items[selected].id);
      items[selected].scrollIntoView({ block: 'nearest' });
    }
  }

  function filterCmdk(query) {
    var q = (query || '').trim().toLowerCase();
    filtered = q
      ? entries.filter(function (entry) { return entry.label.toLowerCase().indexOf(q) !== -1; })
      : entries.slice(0, 12);
    selected = 0;
    renderCmdk();
  }

  function openCmdk() {
    if (!cmdk) { return; }
    cmdk.hidden = false;
    filterCmdk('');
    if (cmdkInput) {
      cmdkInput.value = '';
      cmdkInput.focus();
    }
  }

  function closeCmdk() {
    if (cmdk) {
      cmdk.hidden = true;
      if (searchTrigger) { searchTrigger.focus({ preventScroll: true }); }
    }
  }

  if (searchTrigger) {
    searchTrigger.addEventListener('click', openCmdk);
  }
  if (cmdk) {
    cmdk.addEventListener('click', function (event) {
      if (event.target === cmdk) { closeCmdk(); }
    });
  }
  if (cmdkInput) {
    cmdkInput.addEventListener('input', function () { filterCmdk(cmdkInput.value); });
  }

  document.addEventListener('keydown', function (event) {
    var typing = /^(INPUT|TEXTAREA|SELECT)$/.test(document.activeElement.tagName)
      || document.activeElement.isContentEditable;

    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      if (cmdk && cmdk.hidden) { openCmdk(); } else { closeCmdk(); }
      return;
    }

    if (!cmdk || cmdk.hidden) {
      if (event.key === '/' && !typing && entries.length) {
        event.preventDefault();
        openCmdk();
      }
      return;
    }

    if (event.key === 'Escape') {
      event.preventDefault();
      closeCmdk();
    } else if (event.key === 'ArrowDown') {
      event.preventDefault();
      selected = Math.min(selected + 1, Math.max(filtered.length - 1, 0));
      updateSelection();
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      selected = Math.max(selected - 1, 0);
      updateSelection();
    } else if (event.key === 'Enter') {
      event.preventDefault();
      if (filtered[selected]) { window.location.href = filtered[selected].href; }
    } else if (event.key === 'Tab') {
      // Giữ focus trong hộp thoại (chỉ một input + danh sách).
      event.preventDefault();
      cmdkInput.focus();
    }
  });

  /* ── Auto-dismiss alert sau 6s (giữ nút đóng) ──────────────────────────── */

  Array.prototype.forEach.call(document.querySelectorAll('.alert[role="status"]'), function (alert) {
    window.setTimeout(function () {
      if (alert.parentNode) {
        alert.classList.remove('show');
      }
    }, 6000);
  });

  /* ── Bảng dữ liệu: lọc, sắp xếp, phân trang (client) ───────────────────── */
  /* Không thay đổi dữ liệu máy chủ — chỉ tổ chức lại các dòng đã render.
     Markup: [data-table] > input[data-table-search], bảng, [data-table-pager],
     [data-table-count]. Cột sắp xếp được: <th data-sort>. */

  function normalizeText(value) {
    return String(value == null ? '' : value)
      .normalize('NFD')
      .replace(/[\u0300-\u036f]/g, '')
      .replace(/đ/g, 'd')
      .replace(/Đ/g, 'D')
      .toLowerCase()
      .trim();
  }

  function parseComparable(value) {
    var text = String(value == null ? '' : value).trim();
    var date = text.match(/^(\d{1,2})\/(\d{1,2})\/(\d{4})(?:[ T](\d{1,2}):(\d{2}))?/);
    if (date) {
      return {
        numeric: true,
        value: Date.UTC(
          Number(date[3]), Number(date[2]) - 1, Number(date[1]),
          Number(date[4] || 0), Number(date[5] || 0))
      };
    }
    if (text && /^[-+()\d.,\s₫$%]*$/.test(text) && /\d/.test(text)) {
      var cleaned = text.replace(/[^\d,.-]/g, '').replace(/\./g, '').replace(',', '.');
      var number = Number(cleaned);
      if (!Number.isNaN(number)) {
        return { numeric: true, value: number };
      }
    }
    return { numeric: false, value: text };
  }

  function cellValue(row, index) {
    var cell = row.cells[index];
    if (!cell) { return ''; }
    var explicit = cell.getAttribute('data-value');
    return explicit !== null ? explicit : cell.textContent;
  }

  function compareRows(a, b, index) {
    var left = parseComparable(cellValue(a, index));
    var right = parseComparable(cellValue(b, index));
    if (left.numeric && right.numeric) {
      return left.value - right.value;
    }
    return String(left.value).localeCompare(String(right.value), 'vi', {
      numeric: true,
      sensitivity: 'base'
    });
  }

  function pageNumbers(current, total) {
    var out = [];
    var i;
    if (total <= 7) {
      for (i = 1; i <= total; i += 1) { out.push(i); }
      return out;
    }
    out.push(1);
    var from = Math.max(2, current - 1);
    var to = Math.min(total - 1, current + 1);
    if (from > 2) { out.push('gap'); }
    for (i = from; i <= to; i += 1) { out.push(i); }
    if (to < total - 1) { out.push('gap'); }
    out.push(total);
    return out;
  }

  function initTable(root) {
    var table = root.querySelector('table');
    if (!table || !table.tBodies.length) { return; }

    var tbody = table.tBodies[0];
    var allRows = Array.prototype.slice.call(tbody.rows).filter(function (row) {
      return !row.classList.contains('table-empty') &&
        !row.hasAttribute('hidden') &&
        !row.classList.contains('d-none');
    });
    if (!allRows.length) { return; }

    var search = root.querySelector('[data-table-search]');
    var countEl = root.querySelector('[data-table-count]');
    var pagerEl = root.querySelector('[data-table-pager]');
    var sizeEl = root.querySelector('[data-table-size]');
    var pageSize = parseInt(root.getAttribute('data-page-size') || '10', 10) || 10;
    if (sizeEl) {
      pageSize = parseInt(sizeEl.value, 10) || pageSize;
    }
    if (!pagerEl) {
      pageSize = allRows.length;
    }

    var headers = table.tHead && table.tHead.rows.length
      ? Array.prototype.slice.call(table.tHead.rows[0].cells)
      : [];
    var filtered = allRows.slice();
    var page = 1;
    var sortIndex = -1;
    var sortDirection = 1;
    var emptyRow = null;

    function showEmpty() {
      if (!emptyRow) {
        emptyRow = document.createElement('tr');
        emptyRow.className = 'table-empty';
        var cell = document.createElement('td');
        cell.colSpan = headers.length || 1;

        var wrap = document.createElement('div');
        wrap.className = 'empty-state';
        var title = document.createElement('p');
        title.className = 'empty-state__title';
        title.textContent = 'Không tìm thấy kết quả';
        var note = document.createElement('p');
        note.className = 'empty-state__text';
        note.textContent = 'Thử từ khoá khác hoặc xoá bộ lọc để xem toàn bộ danh sách.';

        wrap.appendChild(title);
        wrap.appendChild(note);
        cell.appendChild(wrap);
        emptyRow.appendChild(cell);
      }
      tbody.appendChild(emptyRow);
      emptyRow.hidden = false;
    }

    if (table.tHead) {
      headers.forEach(function (th, index) {
        if (!th.hasAttribute('data-sort')) { return; }
        th.setAttribute('tabindex', '0');
        th.setAttribute('role', 'button');
        th.setAttribute('aria-sort', 'none');

        function sortBy() {
          if (sortIndex === index) {
            sortDirection = -sortDirection;
          } else {
            sortIndex = index;
            sortDirection = 1;
          }
          headers.forEach(function (header) {
            if (header.hasAttribute('data-sort')) {
              header.setAttribute('aria-sort', 'none');
            }
          });
          th.setAttribute('aria-sort', sortDirection === 1 ? 'ascending' : 'descending');
          filtered.sort(function (a, b) {
            return compareRows(a, b, index) * sortDirection;
          });
          filtered.forEach(function (row) { tbody.appendChild(row); });
          page = 1;
          render();
        }

        th.addEventListener('click', sortBy);
        th.addEventListener('keydown', function (event) {
          if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            sortBy();
          }
        });
      });
    }

    function renderPager(totalPages) {
      if (!pagerEl) { return; }
      pagerEl.textContent = '';

      function addButton(label, targetPage, disabled, current, ariaLabel) {
        var button = document.createElement('button');
        button.type = 'button';
        button.className = 'pager__btn';
        button.textContent = label;
        if (ariaLabel) { button.setAttribute('aria-label', ariaLabel); }
        if (disabled) { button.disabled = true; }
        if (current) { button.setAttribute('aria-current', 'page'); }
        if (!disabled && !current) {
          button.addEventListener('click', function () {
            page = targetPage;
            render();
          });
        }
        pagerEl.appendChild(button);
      }

      addButton('‹', page - 1, page <= 1, false, 'Trang trước');
      pageNumbers(page, totalPages).forEach(function (item) {
        if (item === 'gap') {
          var gap = document.createElement('span');
          gap.className = 'pager__gap';
          gap.textContent = '…';
          pagerEl.appendChild(gap);
          return;
        }
        addButton(String(item), item, false, item === page, 'Trang ' + item);
      });
      addButton('›', page + 1, page >= totalPages, false, 'Trang sau');
    }

    function render() {
      var total = filtered.length;
      var totalPages = Math.max(1, Math.ceil(total / pageSize));
      if (page > totalPages) { page = totalPages; }
      var start = (page - 1) * pageSize;
      var end = start + pageSize;

      allRows.forEach(function (row) { row.hidden = true; });
      filtered.forEach(function (row, index) {
        row.hidden = index < start || index >= end;
      });

      if (!total) {
        showEmpty();
      } else if (emptyRow) {
        emptyRow.hidden = true;
      }

      if (countEl) {
        var query = search ? search.value.trim() : '';
        countEl.textContent = query
          ? total + ' / ' + allRows.length + ' mục'
          : total + ' mục';
      }
      renderPager(totalPages);
    }

    function applyFilter() {
      var query = normalizeText(search ? search.value : '');
      filtered = !query
        ? allRows.slice()
        : allRows.filter(function (row) {
            return normalizeText(row.textContent).indexOf(query) !== -1;
          });
      if (sortIndex >= 0) {
        filtered.sort(function (a, b) {
          return compareRows(a, b, sortIndex) * sortDirection;
        });
        filtered.forEach(function (row) { tbody.appendChild(row); });
      }
      page = 1;
      render();
    }

    if (search) {
      search.addEventListener('input', applyFilter);
      search.addEventListener('search', applyFilter);
    }
    if (sizeEl) {
      sizeEl.addEventListener('change', function () {
        pageSize = parseInt(sizeEl.value, 10) || 10;
        page = 1;
        render();
      });
    }

    render();
  }

  Array.prototype.forEach.call(document.querySelectorAll('[data-table]'), initTable);
})();
