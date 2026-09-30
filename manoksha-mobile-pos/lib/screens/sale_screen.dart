import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:uuid/uuid.dart';

import '../api.dart';
import '../app_state.dart';
import '../printing.dart';
import 'common.dart';
import 'receipt_screen.dart';
import 'scanner_screen.dart';

class CartLine {
  CartLine({required this.skuId, required this.skuCode, required this.name, required this.serialized, required this.retail});

  final String skuId;
  final String skuCode;
  final String name;
  final bool serialized;
  final double retail;
  int quantity = 0;
  final List<String> itemIds = [];
  final List<String> itemCodes = [];
  double? unitPrice;
  String? reason;

  Map<String, dynamic> toJson() => {
    'skuId': skuId,
    'quantity': serialized ? itemIds.length : quantity,
    'itemIds': serialized ? itemIds : null,
    'unitPrice': unitPrice,
    'priceReason': reason,
  };
}

/// A store sale (SPEC §19.3): scan → price (authorized bargaining) → payment(s) → finalize on the server → receipt.
class SaleScreen extends StatefulWidget {
  const SaleScreen({super.key});

  @override
  State<SaleScreen> createState() => _SaleScreenState();
}

class _SaleScreenState extends State<SaleScreen> {
  final _scan = TextEditingController();
  final _scanFocus = FocusNode();
  final List<CartLine> _lines = [];
  Map<String, dynamic>? _quote;
  Timer? _debounce;
  bool _busy = false;
  String? _saleKey;
  String? _keyCart;

  String get _branchId => AppState.instance.branch!.id;

  @override
  void dispose() {
    _debounce?.cancel();
    _scan.dispose();
    _scanFocus.dispose();
    super.dispose();
  }

  List<Map<String, dynamic>> get _payload => _lines.map((l) => l.toJson()).toList();

  void _changed() {
    setState(() {});
    _debounce?.cancel();
    if (_lines.isEmpty) {
      setState(() => _quote = null);
      return;
    }
    _debounce = Timer(const Duration(milliseconds: 350), _requote);
  }

  Future<void> _requote() async {
    try {
      final q = await Api.instance.post('pos/sales/quote', {'branchId': _branchId, 'lines': _payload, 'payments': []}) as Map<String, dynamic>;
      if (mounted) setState(() => _quote = q);
    } catch (e) {
      if (mounted) {
        setState(() => _quote = null);
        showError(context, e);
      }
    }
  }

  Future<void> _addCode(String raw) async {
    final code = raw.trim();
    _scan.clear();
    _scanFocus.requestFocus();
    if (code.isEmpty) return;
    try {
      final s = await Api.instance.get('pos/scan/${Uri.encodeComponent(code)}') as Map<String, dynamic>;
      if (s['retailPrice'] == null) throw ApiException(422, 'NO_PRICE', '${s['productName']} has no price yet and cannot be sold.');
      if (s['availableForRetail'] != true || s['productStatus'] != 'Active') throw ApiException(422, 'NOT_SELLABLE', '${s['productName']} is not for sale.');
      final serialized = s['trackingMode'] == 'Serialized';
      final itemId = s['inventoryItemId'] as String?;
      if (serialized && itemId == null) throw ApiException(400, 'SCAN_PIECE', 'Scan the label on the piece itself.');
      if (itemId != null) {
        if (s['itemStatus'] != 'Available' || s['itemBranchId'] != _branchId) {
          throw ApiException(
            409,
            'PIECE_NOT_AVAILABLE',
            'This piece is ${s['itemStatus']}${s['itemBranchName'] != null ? ' at ${s['itemBranchName']}' : ''} — it cannot be sold here.',
          );
        }
      }
      final line =
          _lines.where((l) => l.skuId == s['skuId']).firstOrNull ??
          (() {
            final l = CartLine(
              skuId: s['skuId'] as String,
              skuCode: s['skuCode'] as String,
              name: '${s['productName']} · ${s['variantName']}',
              serialized: serialized,
              retail: (s['retailPrice'] as num).toDouble(),
            );
            _lines.add(l);
            return l;
          })();
      if (serialized) {
        if (line.itemIds.contains(itemId)) throw ApiException(409, 'ALREADY_SCANNED', 'This piece is already in the bill.');
        line.itemIds.add(itemId!);
        line.itemCodes.add(s['barcode'] as String);
      } else {
        line.quantity++;
      }
      _changed();
    } catch (e) {
      if (mounted) showError(context, e);
    }
  }

  Future<void> _search() async {
    final picked = await showDialog<Map<String, dynamic>>(
      context: context,
      builder: (_) => _SearchDialog(branchId: _branchId),
    );
    if (picked == null) return;
    if (picked['trackingMode'] == 'Serialized') {
      if (mounted) showInfo(context, 'This item is tracked per piece — scan the piece label.');
      return;
    }
    final line = _lines.where((l) => l.skuId == picked['skuId']).firstOrNull;
    if (line != null) {
      line.quantity++;
    } else {
      _lines.add(
        CartLine(
          skuId: picked['skuId'] as String,
          skuCode: picked['skuCode'] as String,
          name: '${picked['productName']} · ${picked['variantName']}',
          serialized: false,
          retail: (picked['price'] as num).toDouble(),
        )..quantity = 1,
      );
    }
    _changed();
  }

  Future<void> _editPrice(CartLine line) async {
    final result = await showDialog<(double?, String?)>(
      context: context,
      builder: (_) => _PriceDialog(line: line),
    );
    if (result == null) return;
    line.unitPrice = result.$1;
    line.reason = result.$2;
    _changed();
  }

  Future<void> _pay() async {
    final quote = _quote;
    if (quote == null) return;
    final total = (quote['grandTotal'] as num).toDouble();
    final paySheet = await showModalBottomSheet<_Payment>(
      context: context,
      isScrollControlled: true,
      builder: (_) => _PaymentSheet(total: total, methods: AppState.instance.paymentMethods),
    );
    if (paySheet == null || !mounted) return;

    Map<String, dynamic>? approval;
    final level = quote['approvalRequired'] as String;
    if (level != 'NONE') {
      approval = await showDialog<Map<String, dynamic>>(
        context: context,
        builder: (_) => _ApprovalDialog(branchId: _branchId, level: level),
      );
      if (approval == null) return;
    }

    // One key per basket: a retry after a dropped connection is the same sale (never sold twice).
    final cart = jsonEncode(_payload);
    if (_saleKey == null || _keyCart != cart) {
      _saleKey = const Uuid().v4();
      _keyCart = cart;
    }
    setState(() => _busy = true);
    try {
      final sale =
          await Api.instance.post(
                'pos/sales',
                {
                  'branchId': _branchId,
                  'lines': _payload,
                  'payments': paySheet.payments,
                  'approval': approval,
                  'customerName': paySheet.customerName,
                  'customerMobile': paySheet.customerMobile,
                },
                {'Idempotency-Key': _saleKey!},
              )
              as Map<String, dynamic>;
      final receipt = await Api.instance.get('pos/sales/${sale['orderId']}/receipt') as Map<String, dynamic>;
      if (!mounted) return;
      setState(() {
        _lines.clear();
        _quote = null;
        _saleKey = null;
      });
      String? printError;
      if (AppState.instance.printerMac != null) printError = await ReceiptPrinter.printReceipt(receipt);
      if (!mounted) return;
      if (printError != null) showError(context, ApiException(0, 'PRINT', 'Sale saved. $printError'));
      await Navigator.of(context).push(MaterialPageRoute(builder: (_) => ReceiptScreen(receipt: receipt)));
    } catch (e) {
      if (!mounted) return;
      if (e is ApiException && e.isNetwork) {
        await showDialog<void>(
          context: context,
          builder: (_) => AlertDialog(
            title: const Text('Not connected'),
            content: Text('${e.message}\n\nThe items are still in the bill. Tap Pay again when the connection is back.'),
            actions: [TextButton(onPressed: () => Navigator.pop(context), child: const Text('OK'))],
          ),
        );
      } else {
        showError(context, e);
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final q = _quote;
    final level = q?['approvalRequired'] as String?;
    return Scaffold(
      appBar: AppBar(title: Text('New sale · ${AppState.instance.branch!.name}')),
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.all(12),
            child: Row(
              children: [
                Expanded(
                  child: TextField(
                    controller: _scan,
                    focusNode: _scanFocus,
                    autofocus: true,
                    decoration: const InputDecoration(labelText: 'Scan or type barcode', prefixIcon: Icon(Icons.qr_code)),
                    onSubmitted: _addCode,
                  ),
                ),
                const SizedBox(width: 8),
                IconButton.filledTonal(
                  tooltip: 'Scan with camera',
                  icon: const Icon(Icons.photo_camera),
                  onPressed: () async {
                    final code = await Navigator.of(context).push<String>(MaterialPageRoute(builder: (_) => const ScannerScreen()));
                    if (code != null) await _addCode(code);
                  },
                ),
                IconButton.filledTonal(tooltip: 'Find item', icon: const Icon(Icons.search), onPressed: _search),
              ],
            ),
          ),
          Expanded(
            child: _lines.isEmpty
                ? const Center(child: Text('Scan an item to start the bill.'))
                : ListView.separated(
                    itemCount: _lines.length,
                    separatorBuilder: (_, _) => const Divider(height: 1),
                    itemBuilder: (_, i) {
                      final l = _lines[i];
                      final qty = l.serialized ? l.itemIds.length : l.quantity;
                      final price = l.unitPrice ?? l.retail;
                      return ListTile(
                        title: Text(l.name),
                        subtitle: Text(
                          '${l.skuCode}${l.serialized ? ' · pieces: ${l.itemCodes.join(', ')}' : ''}${l.unitPrice != null ? '\nMRP ${rs(l.retail)} → ${rs(price)} (${l.reason})' : ''}',
                        ),
                        isThreeLine: l.unitPrice != null,
                        onTap: () => _editPrice(l),
                        trailing: Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            IconButton(
                              icon: const Icon(Icons.remove_circle_outline),
                              onPressed: () {
                                if (l.serialized) {
                                  l.itemIds.removeLast();
                                  l.itemCodes.removeLast();
                                } else {
                                  l.quantity--;
                                }
                                if ((l.serialized ? l.itemIds.length : l.quantity) == 0) _lines.removeAt(i);
                                _changed();
                              },
                            ),
                            Text('$qty', style: const TextStyle(fontSize: 16)),
                            if (!l.serialized)
                              IconButton(
                                icon: const Icon(Icons.add_circle_outline),
                                onPressed: () {
                                  l.quantity++;
                                  _changed();
                                },
                              ),
                            SizedBox(width: 90, child: Text(rs(price * qty), textAlign: TextAlign.right)),
                          ],
                        ),
                      );
                    },
                  ),
          ),
          Material(
            elevation: 8,
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  if (q != null && (q['discountTotal'] as num) > 0)
                    Text(
                      'Discount ${rs(q['discountTotal'] as num)} (max ${(q['maxDiscountPct'] as num).toStringAsFixed(2)}%)',
                      style: const TextStyle(color: Colors.green),
                    ),
                  if (level != null && level != 'NONE')
                    Text(level == 'OWNER' ? 'Needs the Owner\'s approval' : 'Needs a manager\'s approval', style: const TextStyle(color: Colors.orange)),
                  Row(
                    children: [
                      Expanded(
                        child: Text(q == null ? '—' : rs(q['grandTotal'] as num), style: const TextStyle(fontSize: 26, fontWeight: FontWeight.bold)),
                      ),
                      FilledButton.icon(
                        onPressed: q == null || _busy ? null : _pay,
                        icon: _busy ? const SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2)) : const Icon(Icons.payments),
                        label: const Padding(padding: EdgeInsets.symmetric(vertical: 12, horizontal: 8), child: Text('Pay')),
                      ),
                    ],
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _SearchDialog extends StatefulWidget {
  const _SearchDialog({required this.branchId});

  final String branchId;

  @override
  State<_SearchDialog> createState() => _SearchDialogState();
}

class _SearchDialogState extends State<_SearchDialog> {
  List<Map<String, dynamic>> _items = [];

  Future<void> _find(String q) async {
    try {
      final r = await Api.instance.get('pos/items?branchId=${widget.branchId}&q=${Uri.encodeQueryComponent(q)}') as List;
      if (mounted) setState(() => _items = r.cast<Map<String, dynamic>>());
    } catch (e) {
      if (mounted) showError(context, e);
    }
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Find item'),
    content: SizedBox(
      width: 420,
      height: 420,
      child: Column(
        children: [
          TextField(
            autofocus: true,
            decoration: const InputDecoration(labelText: 'Name or SKU'),
            onSubmitted: _find,
          ),
          Expanded(
            child: ListView(
              children: [
                for (final i in _items)
                  ListTile(
                    title: Text('${i['productName']} · ${i['variantName']}'),
                    subtitle: Text('${i['skuCode']} · ${rs(i['price'] as num)} · ${i['availableHere']} here'),
                    enabled: (i['availableHere'] as num) > 0,
                    onTap: () => Navigator.pop(context, i),
                  ),
              ],
            ),
          ),
        ],
      ),
    ),
  );
}

class _PriceDialog extends StatefulWidget {
  const _PriceDialog({required this.line});

  final CartLine line;

  @override
  State<_PriceDialog> createState() => _PriceDialogState();
}

class _PriceDialogState extends State<_PriceDialog> {
  late final _price = TextEditingController(text: (widget.line.unitPrice ?? widget.line.retail).toStringAsFixed(2));
  late final _reason = TextEditingController(text: widget.line.reason ?? '');
  String? _error;

  @override
  Widget build(BuildContext context) {
    final app = AppState.instance;
    final retail = widget.line.retail;
    return AlertDialog(
      title: const Text('Price'),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            app.isOwner
                ? 'MRP ${rs(retail)}.'
                : app.branch!.canApprove || app.branch!.canOverridePrice
                ? 'MRP ${rs(retail)}. You may give up to ${app.branch!.canApprove ? app.managerMaxDiscountPct : app.staffMaxDiscountPct}% yourself; more needs approval.'
                : 'MRP ${rs(retail)}. Any discount needs a manager\'s approval.',
          ),
          const SizedBox(height: 12),
          TextField(
            controller: _price,
            decoration: const InputDecoration(labelText: 'Selling price per unit (Rs.)'),
            keyboardType: const TextInputType.numberWithOptions(decimal: true),
          ),
          const SizedBox(height: 12),
          TextField(
            controller: _reason,
            decoration: const InputDecoration(labelText: 'Reason for discount'),
          ),
          if (_error != null)
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: Text(_error!, style: const TextStyle(color: Colors.red)),
            ),
        ],
      ),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context, (null, null)), child: const Text('Use MRP')),
        FilledButton(
          onPressed: () {
            final p = double.tryParse(_price.text.trim());
            if (p == null || p < 0 || p > retail) return setState(() => _error = 'Enter a price between 0 and the MRP.');
            if (p == retail) return Navigator.pop(context, (null, null));
            if (_reason.text.trim().length < 3) return setState(() => _error = 'Give a reason for the discount.');
            Navigator.pop(context, (double.parse(p.toStringAsFixed(2)), _reason.text.trim()));
          },
          child: const Text('Apply'),
        ),
      ],
    );
  }
}

class _Payment {
  _Payment(this.payments, this.customerName, this.customerMobile);

  final List<Map<String, dynamic>> payments;
  final String? customerName;
  final String? customerMobile;
}

/// Split payments that must add up to the total; only the methods the Owner enabled (setting pos.payment_methods).
class _PaymentSheet extends StatefulWidget {
  const _PaymentSheet({required this.total, required this.methods});

  final double total;
  final List<String> methods;

  @override
  State<_PaymentSheet> createState() => _PaymentSheetState();
}

class _PaymentSheetState extends State<_PaymentSheet> {
  final List<(String, TextEditingController, TextEditingController)> _rows = [];
  final _name = TextEditingController();
  final _mobile = TextEditingController();
  String? _error;

  @override
  void initState() {
    super.initState();
    _addRow(widget.total);
  }

  void _addRow(double amount) => _rows.add((widget.methods.first, TextEditingController(text: amount.toStringAsFixed(2)), TextEditingController()));

  double get _paid => _rows.fold(0, (s, r) => s + (double.tryParse(r.$2.text) ?? 0));

  @override
  Widget build(BuildContext context) => Padding(
    padding: EdgeInsets.fromLTRB(16, 16, 16, MediaQuery.viewInsetsOf(context).bottom + 16),
    child: SingleChildScrollView(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text('Total ${rs(widget.total)}', style: const TextStyle(fontSize: 22, fontWeight: FontWeight.bold)),
          const SizedBox(height: 12),
          for (var i = 0; i < _rows.length; i++)
            Padding(
              padding: const EdgeInsets.only(bottom: 8),
              child: Row(
                children: [
                  DropdownButton<String>(
                    value: _rows[i].$1,
                    items: [for (final m in widget.methods) DropdownMenuItem(value: m, child: Text(m))],
                    onChanged: (m) => setState(() => _rows[i] = (m!, _rows[i].$2, _rows[i].$3)),
                  ),
                  const SizedBox(width: 8),
                  Expanded(
                    child: TextField(
                      controller: _rows[i].$2,
                      decoration: const InputDecoration(labelText: 'Amount'),
                      keyboardType: TextInputType.number,
                      onChanged: (_) => setState(() {}),
                    ),
                  ),
                  const SizedBox(width: 8),
                  if (_rows[i].$1 != 'CASH')
                    Expanded(
                      flex: 2,
                      child: TextField(
                        controller: _rows[i].$3,
                        decoration: const InputDecoration(labelText: 'UPI / transaction ref'),
                      ),
                    ),
                  if (_rows.length > 1) IconButton(icon: const Icon(Icons.close), onPressed: () => setState(() => _rows.removeAt(i))),
                ],
              ),
            ),
          TextButton.icon(
            onPressed: () => setState(() => _addRow((widget.total - _paid).clamp(0, widget.total).toDouble())),
            icon: const Icon(Icons.add),
            label: const Text('Split payment'),
          ),
          Text('Paid ${rs(_paid)} · remaining ${rs(widget.total - _paid)}'),
          const Divider(),
          TextField(
            controller: _name,
            decoration: const InputDecoration(labelText: 'Customer name (optional)'),
          ),
          const SizedBox(height: 8),
          TextField(
            controller: _mobile,
            decoration: const InputDecoration(labelText: 'Customer mobile (optional)'),
            keyboardType: TextInputType.phone,
          ),
          if (_error != null)
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: Text(_error!, style: const TextStyle(color: Colors.red)),
            ),
          const SizedBox(height: 12),
          FilledButton(
            onPressed: () {
              if ((_paid - widget.total).abs() > 0.001) return setState(() => _error = 'Payments must add up to the total.');
              if (_rows.any((r) => r.$1 != 'CASH' && r.$3.text.trim().length < 4)) return setState(() => _error = 'Enter the transaction reference.');
              Navigator.pop(
                context,
                _Payment(
                  [
                    for (final r in _rows) {'method': r.$1, 'amount': double.parse(r.$2.text), 'reference': r.$1 == 'CASH' ? null : r.$3.text.trim()},
                  ],
                  _name.text.trim().isEmpty ? null : _name.text.trim(),
                  _mobile.text.trim().isEmpty ? null : _mobile.text.trim(),
                ),
              );
            },
            child: const Padding(padding: EdgeInsets.all(12), child: Text('Complete sale')),
          ),
        ],
      ),
    ),
  );
}

/// The approver chooses themselves and enters their own PIN on this device (never the seller).
class _ApprovalDialog extends StatefulWidget {
  const _ApprovalDialog({required this.branchId, required this.level});

  final String branchId;
  final String level;

  @override
  State<_ApprovalDialog> createState() => _ApprovalDialogState();
}

class _ApprovalDialogState extends State<_ApprovalDialog> {
  List<Map<String, dynamic>>? _approvers;
  String? _selected;
  final _pin = TextEditingController();

  @override
  void initState() {
    super.initState();
    Api.instance
        .get('pos/approvers?branchId=${widget.branchId}&level=${widget.level}')
        .then((r) {
          if (mounted) setState(() => _approvers = (r as List).cast<Map<String, dynamic>>());
        })
        .catchError((Object e) {
          if (mounted) showError(context, e);
        });
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: Text(widget.level == 'OWNER' ? 'Owner approval' : 'Manager approval'),
    content: _approvers == null
        ? const SizedBox(height: 80, child: Busy())
        : RadioGroup<String>(
            groupValue: _selected,
            onChanged: (v) => setState(() => _selected = v),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                if (_approvers!.isEmpty) const Text('Nobody who can approve this is available. Reduce the discount or contact the Owner.'),
                for (final a in _approvers!)
                  RadioListTile<String>(
                    value: a['userId'] as String,
                    enabled: a['hasPin'] == true,
                    title: Text(a['displayName'] as String),
                    subtitle: a['hasPin'] == true ? null : const Text('No approval PIN set'),
                  ),
                TextField(
                  controller: _pin,
                  decoration: const InputDecoration(labelText: 'Approver PIN'),
                  obscureText: true,
                  keyboardType: TextInputType.number,
                  maxLength: 6,
                ),
              ],
            ),
          ),
    actions: [
      TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel')),
      FilledButton(
        onPressed: _selected == null ? null : () => Navigator.pop(context, {'approverUserId': _selected, 'pin': _pin.text}),
        child: const Text('Approve'),
      ),
    ],
  );
}
