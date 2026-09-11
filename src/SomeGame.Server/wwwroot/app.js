(() => {
  const $ = (id) => document.getElementById(id);
  const screens = {
    lobby: $('screen-lobby'),
    wait: $('screen-lobby-wait'),
    game: $('screen-game'),
  };
  const state = {
    ws: null,
    seat: -1,
    view: null,
    objective: '',
    logs: [],
    myRole: '',
    myRoleKey: '',
    myColor: '',
    myCode: '',
    host: false,
    pending: null, // {kind:'freeCard'|'checkCard', cardId, defId}
    busy: false,
    inGame: false,
  };

  function connect() {
    const proto = location.protocol === 'https:' ? 'wss:' : 'ws:';
    const ws = new WebSocket(`${proto}//${location.host}/ws`);
    state.ws = ws;
    ws.onmessage = (ev) => { try { handle(JSON.parse(ev.data)); } catch (e) { console.error(e); } };
    ws.onclose = () => { $('overlay-box').innerHTML = '<h2>连接断开</h2><p>请刷新页面重试。</p>'; showOverlay(); };
  }

  function send(obj) { if (state.ws && state.ws.readyState === 1) state.ws.send(JSON.stringify(obj)); }

  function show(screen) { Object.values(screens).forEach(s => s.style.display = 'none'); screens[screen].style.display = ''; }

  function showOverlay() { $('overlay').style.display = 'flex'; }
  function hideOverlay() { $('overlay').style.display = 'none'; }

  function handle(msg) {
    if (msg.type === 'lobby' && state.inGame) return; // 对局开始后忽略大厅广播
    switch (msg.type) {
      case 'lobby':
        state.host = msg.isHost;
        renderLobby(msg);
        show('wait');
        break;
      case 'info':
        appendLog({ text: msg.text, kind: 'info', tier: 0 });
        break;
      case 'objective':
        state.objective = msg.text;
        break;
      case 'log':
        appendLog(msg);
        break;
      case 'phase':
        appendLog({ text: msg.text, kind: 'phase', tier: 0 });
        break;
      case 'view':
        state.view = msg;
        state.seat = msg.seat;
        state.myRole = msg.role;
        state.myRoleKey = msg.roleKey;
        state.myColor = msg.roleColor;
        state.myCode = msg.myCode;
        if (msg.objective) state.objective = msg.objective;
        state.inGame = true;
        show('game');
        scheduleRender();
        break;
      case 'checkresult':
        appendLog({
          text: `查验「${msg.targetCode}」：${msg.identity}${msg.enemy ? '（敌对）' : ''}${msg.rangeOk ? '（在交战范围内）' : '（超出交战范围）'}${msg.note ? '；' + msg.note : ''}`,
          kind: 'info', tier: 0,
        });
        if (msg.enemy && msg.canBattle) {
          appendLog({ text: '对方是敌对身份，是否发起战斗？', kind: 'info', tier: 0 });
        }
        break;
      case 'gameover':
        renderGameOver(msg);
        break;
    }
  }

  function appendLog(m) {
    const el = document.createElement('div');
    el.className = 't' + (m.tier ?? 0) + (m.kind ? ' ' + m.kind : '');
    el.textContent = m.text;
    const box = $('logs');
    box.appendChild(el);
    box.scrollTop = box.scrollHeight;
    if (box.childNodes.length > 500) box.removeChild(box.firstChild);
  }

  // ---------------- 大厅 ----------------
  function renderLobby(msg) {
    $('lobby-room').textContent = msg.roomId;
    const box = $('lobby-seats');
    box.innerHTML = '';
    msg.seats.forEach((s) => {
      const row = document.createElement('div');
      row.className = 'seat-row';
      row.textContent = `${s.seat + 1} 号位：${s.name}${s.bot ? '（机器人）' : ''}`;
      box.appendChild(row);
    });
    $('lobby-hint').textContent = state.host ? '你是房主，可随时开始（空位自动补机器人）。' : `邀请码：${msg.roomId}，等待房主开始…`;
    $('btn-start').style.display = state.host ? '' : 'none';
  }

  // ---------------- 对局渲染 ----------------
  function render() {
    const v = state.view;
    if (!v) return;
    $('s-round').textContent = v.round;
    $('s-stage').textContent = stageText(v.stage);
    $('s-hp').textContent = v.hp;
    $('s-ap').textContent = v.ap;
    $('s-countdown').textContent = v.protectedCountdown >= 0 ? v.protectedCountdown : '?';
    const ext = $('s-extraction');
    ext.textContent = v.extractionHint || (v.extractionVisible ? '★' : '未知');
    ext.classList.toggle('revealed', !!v.extractionVisible);
    $('objective').textContent = `${state.myRole}${state.myColor ? `（${state.myColor}案）` : ''} — ${state.objective}`;
    $('effects').textContent = v.effects.length ? '状态：' + v.effects.join('、') : '';
    if (v.medkitHints && v.medkitHints.length) {
      $('effects').textContent += (v.effects.length ? ' ｜ ' : '') + '附近医疗包：' + v.medkitHints.join(' ');
    }
    renderCellPanel(v);
    renderHand(v);
    renderActions(v);
    renderBattle(v);
    // 棋盘渲染较重（全图），仅当盘面相关内容变化时重建
    const sig = boardSig(v);
    if (sig !== lastBoardSig) {
      lastBoardSig = sig;
      renderBoard(v);
    }
  }

  let lastBoardSig = '';
  let renderScheduled = false;
  function boardSig(v) {
    const p = state.pending || {};
    const pSig = (p.kind || '') + (p.dash ? 'd' : '') + (p.defId || '');
    return (v.x + ',' + v.y + '|' + v.round + '|' + v.extractionExact + '|' + pSig + '|' +
      (v.cells || []).map(c => c.x + ',' + c.y + ':' + c.tier + ':' +
        (c.occupants || []).join('/') + ':' + (c.items || []).join('/')).join(';') +
      '|' + (v.markers || []).map(m => m.x + ',' + m.y + ':' + m.text).join(';'));
  }
  // 多条 view 在短时间内到达时合并为一次渲染（setTimeout 保证后台也执行）
  function scheduleRender() {
    if (renderScheduled) return;
    renderScheduled = true;
    setTimeout(() => { renderScheduled = false; render(); }, 16);
  }

  function stageText(s) {
    return { Free: '自由行动', Check: '身份查验', Battle: '战斗', Terminal: '已结束', Idle: '—' }[s] || s;
  }

  function isCellTarget(p) {
    return p && ['drone', 'track', 'molotov'].includes(p.defId);
  }

  function pendingHint(p) {
    if (!p) return '';
    if (p.kind === 'move') return p.dash ? '选择疾走目标（距离 1-5 格）' : '选择移动目标（距离 1-3 格）';
    if (p.kind === 'cover') return '选择同格角色进行掩护';
    if (p.defId === 'glue') return '选择本格物品布置陷阱';
    if (['heal', 'mimic', 'dye'].includes(p.defId)) return '选择同格角色作为目标';
    if (['drone', 'track', 'molotov'].includes(p.defId)) return '全图选择：点击地图格子指定目标';
    return '';
  }

  function renderBoard(v) {
    const board = $('board');
    const hint = pendingHint(state.pending);
    const hintEl = $('board-hint');
    hintEl.textContent = hint;
    hintEl.style.display = hint ? '' : 'none';
    const cells = v.cells || [];
    const maxX = Math.max(...cells.map(c => c.x), 0);
    const maxY = Math.max(...cells.map(c => c.y), 0);
    board.style.gridTemplateColumns = `24px repeat(${maxX + 1}, var(--cellsize))`;
    board.innerHTML = '';
    const byKey = {};
    cells.forEach(c => byKey[c.x + ',' + c.y] = c);
    for (let gy = 0; gy <= maxY + 1; gy++) for (let gx = 0; gx <= maxX + 1; gx++) {
      if (gy === 0 && gx === 0) {
        const a = document.createElement('div');
        a.className = 'axis';
        a.textContent = 'X→';
        board.appendChild(a);
        continue;
      }
      if (gy === 0) {
        const a = document.createElement('div');
        a.className = 'axis';
        a.textContent = gx - 1;
        board.appendChild(a);
        continue;
      }
      if (gx === 0) {
        const a = document.createElement('div');
        a.className = 'axis';
        a.textContent = 'Y ' + (gy - 1);
        board.appendChild(a);
        continue;
      }
      const x = gx - 1, y = gy - 1;
      const c = byKey[x + ',' + y];
      const div = document.createElement('div');
      if (!c) { div.className = 'cell'; board.appendChild(div); continue; }
      div.className = 'cell tier' + c.tier + (x === v.x && y === v.y ? ' me' : '') +
        (x === v.extractionX && y === v.extractionY && v.extractionExact ? ' extraction' : '') +
        (c.burning ? ' burning' : '') + (c.smoky ? ' smoky' : '');
      if (x === v.extractionX && y === v.extractionY && v.extractionExact) {
        const t = document.createElement('span');
        t.className = 'who';
        t.textContent = '★撤离点';
        div.appendChild(t);
      }
      if (c.tier >= 0) {
        if (c.occupants.length) {
          c.occupants.forEach(code => {
            const t = document.createElement('span');
            t.className = 'who clickable-occ';
            t.textContent = code;
            t.onclick = (e) => {
              // 移动/选格目标时，让点击落到格子本身（多人同格也不阻挡选格）
              if (state.pending && (state.pending.kind === 'move' || isCellTarget(state.pending))) return;
              e.stopPropagation();
              occupantClick(code);
            };
            div.appendChild(t);
          });
        }
        (c.items || []).forEach(it => {
          const s = document.createElement('span');
          s.className = 'item';
          s.textContent = it;
          div.appendChild(s);
        });
      }
      const marker = (v.markers || []).find(m => m.x === x && m.y === y);
      if (marker) {
        const s = document.createElement('span');
        s.className = 'marker';
        s.textContent = '▼' + marker.text;
        div.appendChild(s);
      }
      const clickable = cellClickable(c, v);
      if (clickable) {
        div.classList.add('clickable', 'target');
        div.onclick = () => onCellClick(x, y, c);
      }
      board.appendChild(div);
    }
  }

  function cellClickable(c, v) {
    if (state.pending) {
      const p = state.pending;
      if (p.kind === 'move') {
        const d = Math.abs(c.x - v.x) + Math.abs(c.y - v.y);
        return d >= 1 && d <= (p.dash ? 5 : 3);
      }
      if (isCellTarget(p)) return true;
      return false;
    }
    return false;
  }

  function onCellClick(x, y, cell) {
    const v = state.view;
    if (!v) return;
    if (state.pending) {
      const p = state.pending;
      if (p.kind === 'move') {
        send({ t: 'cmd', op: 'move', x, y, dash: !!p.dash });
        state.pending = null;
        render();
        return;
      }
      if (p.kind === 'checkCard') {
        send({ t: 'cmd', op: 'card', cardId: p.cardId, x, y });
        state.pending = null;
        return;
      }
      if (p.kind === 'freeCard') {
        send({ t: 'cmd', op: 'card', cardId: p.cardId, x, y });
        state.pending = null;
        return;
      }
      return;
    }
  }

  function occupantClick(code) {
    const v = state.view;
    if (!v) return;
    if (code === v.myCode) { appendLog({ text: '不能对自己执行此操作。', kind: 'info', tier: 0 }); return; }
    if (state.pending) {
      const p = state.pending;
      if (p.kind === 'cover') {
        send({ t: 'cmd', op: 'cover', target: code });
        state.pending = null;
        render();
        return;
      }
      if (p.kind === 'checkCard' && (p.defId === 'heal' || p.defId === 'medkit' || p.defId === 'medkit_temp')) {
        send({ t: 'cmd', op: 'card', cardId: p.cardId, target: code });
        state.pending = null;
        return;
      }
      if (p.kind === 'freeCard') {
        send({ t: 'cmd', op: 'card', cardId: p.cardId, target: code });
        state.pending = null;
        return;
      }
      return; // 其他 pending（移动/全图选择）不触发交谈/查验
    }
    if (v.awaitKind === 'FreeAction' && v.yourTurn) {
      send({ t: 'cmd', op: 'talk', target: code });
    } else if (v.awaitKind === 'CheckAction' && v.yourTurn) {
      send({ t: 'cmd', op: 'check', target: code });
    }
  }

  function itemClick(item, action) {
    const v = state.view;
    if (state.pending) {
      const p = state.pending;
      if (p.kind === 'freeCard' && p.defId === 'glue') {
        send({ t: 'cmd', op: 'card', cardId: p.cardId, itemId: item.id });
        state.pending = null;
        return;
      }
      return; // 其他 pending（移动/全图选择）不触发物品操作
    }
    if (v.awaitKind === 'FreeAction' && v.yourTurn) {
      send({ t: 'cmd', op: action, itemId: item.id });
    }
  }

  function renderCellPanel(v) {
    const panel = $('cell-panel');
    panel.innerHTML = '';
    const me = (v.cells || []).find(c => c.x === v.x && c.y === v.y);
    const title = document.createElement('div');
    title.className = 'hint';
    title.textContent = `你位于 [${v.x},${v.y}]`;
    panel.appendChild(title);
    if (!me) return;

    const canAct = v.yourTurn;
    const pendingCover = state.pending && state.pending.kind === 'cover';
    const pendingActor = state.pending && (['heal', 'medkit', 'medkit_temp', 'mimic', 'dye'].includes(state.pending.defId));
    const pendingItem = state.pending && state.pending.defId === 'glue';
    const showTalk = canAct && v.awaitKind === 'FreeAction';
    const showCheck = canAct && v.awaitKind === 'CheckAction';

    // 角色：点击交谈 / 查验 / 作为卡牌目标（自身除外）
    if (me.occupants.length) {
      const row = document.createElement('div');
      row.className = 'cell-occupants';
      // 治疗包/医疗包可对自己使用
      if (state.pending && (state.pending.defId === 'heal' || state.pending.defId === 'medkit' || state.pending.defId === 'medkit_temp') && canAct) {
        const selfChip = document.createElement('button');
        selfChip.className = 'chip clickable';
        selfChip.textContent = v.myCode + '（自己）';
        selfChip.onclick = () => {
          send({ t: 'cmd', op: 'card', cardId: state.pending.cardId, target: v.myCode });
          state.pending = null;
          render();
        };
        row.appendChild(selfChip);
      }
      me.occupants.forEach(code => {
        const chip = document.createElement('button');
        const isSelf = code === v.myCode;
        const pendingBlocks = state.pending && !(pendingActor || pendingCover); // 移动/全图选择时角色不可点
        const clickable = !isSelf && canAct && !pendingBlocks && (showTalk || showCheck || pendingActor || pendingCover);
        chip.className = 'chip' + (clickable ? ' clickable' : '');
        chip.textContent = code + (isSelf ? '（你）' : '');
        if (clickable) chip.onclick = () => occupantClick(code);
        row.appendChild(chip);
      });
      panel.appendChild(row);
    } else {
      const none = document.createElement('div');
      none.className = 'hint';
      none.textContent = '此处没有其他角色';
      panel.appendChild(none);
    }

    // 可交互物品：开箱 / 医疗包 / 万能胶目标
    if (me.interactables && me.interactables.length) {
      const row = document.createElement('div');
      row.className = 'cell-items';
      me.interactables.forEach(it => {
        const b = document.createElement('button');
        let label = it.label;
        let clickable = false;
        let action = null;
        const pendingBlocks = state.pending && !(pendingItem); // 移动/全图选择时物品不可操作
        if (pendingItem && canAct) { label = `对「${it.label}」布置陷阱`; clickable = true; action = () => itemClick(it, 'glue'); }
        else if (!pendingBlocks && it.kind === 'Chest' && canAct) { label = it.label; clickable = true; action = () => itemClick(it, 'open'); }
        else if (!pendingBlocks && it.kind === 'Medkit' && canAct) { label = '拾取医疗包'; clickable = true; action = () => itemClick(it, 'medkit'); }
        b.className = 'cellitem' + (clickable ? ' clickable' : '');
        b.textContent = label;
        if (action) b.onclick = action;
        row.appendChild(b);
      });
      panel.appendChild(row);
    }

    if (pendingCover) {
      const hint = document.createElement('div');
      hint.className = 'hint';
      hint.textContent = '点击同格角色进行掩护';
      panel.appendChild(hint);
    } else if (pendingActor) {
      const hint = document.createElement('div');
      hint.className = 'hint';
      hint.textContent = '请点击同格角色作为目标';
      panel.appendChild(hint);
    } else if (showTalk) {
      const hint = document.createElement('div');
      hint.className = 'hint';
      hint.textContent = '点击角色可交谈';
      panel.appendChild(hint);
    } else if (showCheck) {
      const hint = document.createElement('div');
      hint.className = 'hint';
      hint.textContent = '点击角色查验身份';
      panel.appendChild(hint);
    }
  }

  function renderHand(v) {
    const box = $('hand');
    box.innerHTML = '';
    (v.hand || []).forEach(c => {
      const el = document.createElement('div');
      el.className = 'card' + (c.temp ? ' temp' : '') + (c.usable ? ' usable' : '');
      el.textContent = c.name + (c.temp ? '（临）' : '');
      if (c.usable) {
        el.onclick = () => onCardClick(c);
        if (state.pending && state.pending.cardId === c.id) el.classList.add('pending');
      }
      box.appendChild(el);
    });
  }

  function targetOf(defId) {
    if (['heal', 'medkit', 'medkit_temp', 'mimic', 'dye'].includes(defId)) return 'actor';
    if (defId === 'glue') return 'item';
    if (['drone', 'molotov', 'track'].includes(defId)) return 'cell';
    return 'self';
  }

  function onCardClick(card) {
    const v = state.view;
    if (!v || !v.yourTurn) return;
    const defId = card.defId;
    if (v.awaitKind === 'BattleAction') {
      send({ t: 'cmd', action: 'play', cardId: card.id });
      return;
    }
    if (v.awaitKind === 'BattleDefense') {
      if (card.defId === 'dodge') send({ t: 'cmd', dodge: true, cardId: card.id });
      else appendLog({ text: '防御阶段只能使用「闪避」。', kind: 'info', tier: 0 });
      return;
    }
    const kind = v.awaitKind === 'CheckAction' ? 'checkCard' : 'freeCard';
    const tgt = targetOf(defId);
    const isHeal = defId === 'heal' || defId === 'medkit' || defId === 'medkit_temp';
    if (tgt === 'self') {
      send({ t: 'cmd', op: 'card', cardId: card.id });
    } else if (isHeal) {
      // 治疗包/医疗包：无其他同格角色时直接治疗自己，否则让玩家选择目标（含自己）
      const me = (v.cells || []).find(c => c.x === v.x && c.y === v.y);
      const others = (me?.occupants || []).filter(c => c !== v.myCode);
      if (!others.length) {
        send({ t: 'cmd', op: 'card', cardId: card.id });
        state.pending = null;
        render();
        return;
      }
      state.pending = { kind, cardId: card.id, defId };
      appendLog({ text: '请选择治疗目标：点击同格角色或「自己」。', kind: 'info', tier: 0 });
      render();
    } else {
      state.pending = { kind, cardId: card.id, defId };
      appendLog({ text: `请选择目标${tgt === 'cell' ? '（点击地图格子）' : tgt === 'item' ? '（点击本格物品）' : '（点击同格角色）'}`, kind: 'info', tier: 0 });
      render();
    }
  }

  function renderActions(v) {
    const box = $('actions');
    box.innerHTML = '';
    if (!v.yourTurn) {
      const s = document.createElement('span');
      s.className = 'hint';
      s.textContent = '等待其他玩家…';
      box.appendChild(s);
      return;
    }
    const btn = (label, cls, fn) => {
      const b = document.createElement('button');
      b.textContent = label;
      b.className = cls || '';
      b.onclick = fn;
      box.appendChild(b);
    };

    if (v.awaitKind === 'FreeAction') {
      btn('移动（1-3格）', '', () => {
        state.pending = { kind: 'move', dash: false };
        appendLog({ text: '请点击目标格子（距离 1-3 格）。', kind: 'info', tier: 0 });
        render();
      });
      btn('疾走（2AP，1-5格）', '', () => {
        state.pending = { kind: 'move', dash: true };
        appendLog({ text: '请点击目标格子（疾走距离 1-5 格）。', kind: 'info', tier: 0 });
        render();
      });
      btn('探查', '', () => send({ t: 'cmd', op: 'inspect' }));
      btn('交谈/物品', '', () => appendLog({ text: '请在下方「所在格」面板点击角色或物品。', kind: 'info', tier: 0 }));
      if (state.myRoleKey === 'thief') btn('窃取（需与目标案贵宾同格）', '', () => send({ t: 'cmd', op: 'steal' }));
      if (state.myRoleKey === 'bodyguard') {
        btn('掩护（0AP，选择本格角色）', '', () => {
          state.pending = { kind: 'cover' };
          appendLog({ text: '请点击「所在格」面板中的角色进行掩护。', kind: 'info', tier: 0 });
          render();
        });
      }
      if (['bodyguard', 'killer', 'thief'].includes(state.myRoleKey)) {
        btn('标注（0AP）', '', () => send({ t: 'cmd', op: 'mark', msg: '标记' }));
        btn('求援（0AP）', '', () => send({ t: 'cmd', op: 'mark', msg: '求援：请来支援' }));
      }
      btn('结束本轮行动', 'primary', () => send({ t: 'cmd', op: 'finish' }));
    } else if (v.awaitKind === 'CheckAction') {
      btn('跳过', '', () => send({ t: 'cmd', op: 'skip' }));
      btn('查验（点击「所在格」角色或输入远处代号）', '', () => appendLog({ text: '请点击一个角色代号以查验。', kind: 'info', tier: 0 }));
      const input = document.createElement('input');
      input.placeholder = '远处目标代号';
      const go = document.createElement('button');
      go.textContent = '查验';
      go.onclick = () => { if (input.value) send({ t: 'cmd', op: 'check', target: input.value }); };
      box.appendChild(input);
      box.appendChild(go);
    } else if (v.awaitKind === 'CheckConfirm') {
      btn('发起战斗', 'primary', () => send({ t: 'cmd', startBattle: true }));
      btn('不开战', '', () => send({ t: 'cmd', startBattle: false }));
    } else if (v.awaitKind === 'BattleAction') {
      btn('放弃（连续两次放弃则中止战斗）', '', () => send({ t: 'cmd', action: 'pass' }));
    } else if (v.awaitKind === 'BattleDefense') {
      btn('承受这次攻击', '', () => send({ t: 'cmd', dodge: false }));
      const dodge = (v.hand || []).find(c => c.defId === 'dodge');
      if (dodge) btn('使用闪避', 'primary', () => send({ t: 'cmd', dodge: true, cardId: dodge.id }));
    }

    if (state.pending) {
      const cancel = document.createElement('button');
      cancel.textContent = '取消选择';
      cancel.onclick = () => { state.pending = null; render(); };
      box.appendChild(cancel);
    }
  }

  function renderBattle(v) {
    const panel = $('battle-panel');
    if (v.inBattle) {
      panel.style.display = '';
      panel.textContent = v.battlePrompt;
    } else panel.style.display = 'none';
  }

  function renderGameOver(g) {
    const box = $('overlay-box');
    let html = `<h2>对局结束</h2><p class="hint">${g.reason}</p><ul>`;
    g.results.forEach(r => {
      html += `<li>${r.name} ${r.bot ? '（机器人）' : ''} = ${r.role}${r.color ? `（${r.color}案）` : ''} — <b>${r.win ? '胜利' : '失败'}</b></li>`;
    });
    html += '</ul><h3>全场身份</h3><p>' + g.reveal.join('；') + '</p>';
    html += '<p><button id="btn-again" class="primary">返回首页</button></p>';
    box.innerHTML = html;
    showOverlay();
    document.getElementById('btn-again').onclick = () => location.reload();
  }

  // ---------------- 决策倒计时 ----------------
  function updateCountdown() {
    const el = $('s-turn-time');
    const v = state.view;
    if (!el || !v || !v.deadlineEpochMs || !v.yourTurn) {
      if (el) { el.textContent = v && v.yourTurn ? '…' : '—'; el.classList.remove('urgent'); }
      return;
    }
    const rem = Math.max(0, Math.round((v.deadlineEpochMs - Date.now()) / 1000));
    el.textContent = rem + 's';
    el.classList.toggle('urgent', rem <= 10);
  }

  $('btn-create').onclick = () => send({
    t: 'create',
    playerName: $('create-name').value || '房主',
    players: parseInt($('create-players').value, 10),
  });
  $('btn-join').onclick = () => send({
    t: 'join',
    playerName: $('join-name').value || '玩家',
    roomId: $('join-room').value.trim(),
  });
  $('btn-start').onclick = () => send({ t: 'start' });

  connect();
  setInterval(updateCountdown, 250);
})();